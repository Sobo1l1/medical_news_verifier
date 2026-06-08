using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services.Parsers;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Services;

public partial class NewsAnalysisService(
    AppDbContext db,
    IPythonLinguisticClient pythonClient,
    IRelevantCorpusService relevantCorpusService,
    IOllamaComparisonClient ollamaClient,
    IAnalysisJobStore jobStore,
    IAnalysisDefaultsService analysisDefaultsService,
    IWebHostEnvironment env,
    IConfiguration configuration,
    ILogger<NewsAnalysisService> logger) : INewsAnalysisService
{
    public async Task<AnalysisRecord?> GetAnalysisByIdAsync(int id, CancellationToken cancellationToken) =>
        await db.AnalysisRecords
            .Include(r => r.NewsSubmission)
            .Include(r => r.SuspiciousFragments)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<List<OfficialPublicationMatchVm>> GetOfficialMatchesAsync(
        string newsText,
        CancellationToken cancellationToken) =>
        await GetOfficialMatchesInternalAsync(string.Empty, newsText, cancellationToken);

    public Task<List<OfficialPublicationMatchVm>> GetMatchesForAnalysisAsync(
        int analysisRecordId,
        CancellationToken cancellationToken) =>
        GetStoredMatchesAsync(analysisRecordId, cancellationToken);

    public async Task<(AnalysisRecord record, List<OfficialPublicationMatchVm> matches, bool isFromHistory)> AnalyzeAndSaveAsync(
        AnalyzeNewsInputModel input,
        bool forceNew,
        CancellationToken cancellationToken)
    {
        if (!forceNew)
        {
            var (existing, historyMatches) = await TryReuseExistingRecordAsync(input, cancellationToken);
            if (existing is not null)
            {
                return (existing, historyMatches, true);
            }
        }

        var (record, matches) = await AnalyzeAndPersistAsync(
            input,
            cancellationToken,
            runSettings: analysisDefaultsService.Resolve(input.RunSettings));
        return (record, matches, false);
    }

    public async Task RunAnalysisJobAsync(Guid jobId, AnalyzeNewsInputModel input, CancellationToken cancellationToken)
    {
        try
        {
            jobStore.Patch(jobId, s =>
            {
                s.Phase = "LoadingSources";
                s.StepIndex = 0;
                s.Message = "Поиск релевантных материалов в официальных источниках…";
            });

            var forceNew = input.ForceNew;
            if (!forceNew)
            {
                var (existing, matches) = await TryReuseExistingRecordAsync(input, cancellationToken);
                if (existing is not null)
                {
                    jobStore.Patch(jobId, s =>
                    {
                        s.Phase = "Completed";
                        s.FeaturesCompleted = true;
                        s.NeuralCompleted = true;
                        s.StepIndex = 4;
                        s.Message = "Найдена сохранённая проверка с теми же заголовком и текстом.";
                        s.HeuristicScore = existing.HeuristicReliabilityScore > 0
                            ? existing.HeuristicReliabilityScore
                            : existing.ReliabilityScore;
                        s.LlmScore = existing.LlmAlignmentScore;
                        s.CombinedScore = existing.ReliabilityScore;
                        s.RecordId = existing.Id;
                        s.LlmSummaryPreview = existing.LlmSummary;
                    });
                    return;
                }
            }

            var runSettings = analysisDefaultsService.Resolve(input.RunSettings);

            var publications = await LoadPublicationsCorpusAsync(
                input.Headline,
                input.NewsText,
                runSettings,
                cancellationToken);
            jobStore.Patch(jobId, s =>
            {
                s.Phase = "RunningAnalyzers";
                s.StepIndex = 1;
                s.FeaturesCompleted = false;
                s.NeuralCompleted = false;
                s.Message = "Параллельно выполняются признаковый анализ (Python) и нейросетевое сравнение с корпусом (Ollama)…";
            });

            var (record, jobMatches) = await AnalyzeAndPersistAsync(
                input,
                cancellationToken,
                publications,
                runSettings,
                jobId);

            jobStore.Patch(jobId, s =>
            {
                s.Phase = "Completed";
                s.FeaturesCompleted = true;
                s.NeuralCompleted = true;
                s.RecordId = record.Id;
                s.CombinedScore = record.ReliabilityScore;
                s.Message = "Анализ завершён.";
            });
        }
        catch (OperationCanceledException)
        {
            jobStore.Patch(jobId, s =>
            {
                s.Phase = "Failed";
                s.Error = "Операция отменена.";
            });
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis job {JobId} failed", jobId);
            jobStore.Patch(jobId, s =>
            {
                s.Phase = "Failed";
                s.Error = ex.Message;
            });
        }
    }

    private async Task<(AnalysisRecord? record, List<OfficialPublicationMatchVm> matches)> TryReuseExistingRecordAsync(
        AnalyzeNewsInputModel input,
        CancellationToken cancellationToken)
    {
        var fingerprint = NewsContentFingerprint.Compute(input.Headline, input.NewsText);
        var existing = await db.AnalysisRecords
            .Include(r => r.NewsSubmission)
            .Include(r => r.SuspiciousFragments)
            .Where(r => r.NewsSubmission!.ContentFingerprint == fingerprint)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return (null, new List<OfficialPublicationMatchVm>());
        }

        var historyMatches = await GetStoredMatchesAsync(existing.Id, cancellationToken);
        return (existing, historyMatches);
    }

    private async Task<(AnalysisRecord record, List<OfficialPublicationMatchVm> matches)> AnalyzeAndPersistAsync(
        AnalyzeNewsInputModel input,
        CancellationToken cancellationToken,
        List<OfficialPublication>? publications = null,
        EffectiveAnalysisRunSettings? runSettings = null,
        Guid? jobId = null)
    {
        runSettings ??= analysisDefaultsService.Resolve(input.RunSettings);
        var doc = AnalyzedDocument.From(input.Headline, input.NewsText);
        var fullText = doc.FullText;
        var lexicons = LoadLexicons();
        var weights = LoadWeights();
        var pubs = publications ?? await LoadPublicationsCorpusAsync(
            input.Headline,
            input.NewsText,
            runSettings,
            cancellationToken);
        var maxCorpus = runSettings.MaxCorpusSnippets;

        var matches = RankMatchesToViewModels(input.Headline, input.NewsText, pubs);
        var corpusForLlm = SelectCorpusForLlm(input.Headline, input.NewsText, pubs, maxCorpus);

        if (jobId.HasValue)
        {
            jobStore.Patch(jobId.Value, s =>
            {
                s.Message = "Параллельно выполняются признаковый анализ (Python) и нейросетевое сравнение с корпусом (Ollama)…";
            });
        }

        var (lexical, lexicalFragments) = AnalyzeLexicalWithSpans(fullText, lexicons);
        var pythonTask = pythonClient.AnalyzeAsync(fullText, runSettings, cancellationToken);
        var llmTask = ollamaClient.CompareNewsToCorpusAsync(
            input.Headline,
            input.NewsText,
            corpusForLlm,
            runSettings,
            cancellationToken);

        PythonAnalysisOutcome? pythonOutcome = null;
        OllamaComparisonOutcome? llmOutcome = null;
        int? heuristicScore = null;
        var pending = new List<Task> { pythonTask, llmTask };

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);

            if (finished == pythonTask)
            {
                pythonOutcome = await pythonTask;
                var pythonFragments = pythonOutcome.Fragments;
                heuristicScore = Math.Clamp(
                    CalculateScore(weights, lexical, pythonFragments, matches),
                    0,
                    100);

                if (jobId.HasValue)
                {
                    jobStore.Patch(jobId.Value, s =>
                    {
                        s.Phase = "HeuristicReady";
                        s.FeaturesCompleted = true;
                        s.StepIndex = s.NeuralCompleted ? 3 : 2;
                        s.HeuristicScore = heuristicScore;
                        s.Message = s.NeuralCompleted
                            ? "Признаковый и нейросетевой анализ завершены. Формируется итог…"
                            : "Признаковый анализ завершён. Выполняется нейросетевой анализ (Ollama)…";
                    });
                }
            }
            else if (finished == llmTask)
            {
                llmOutcome = await llmTask;
                if (jobId.HasValue)
                {
                    jobStore.Patch(jobId.Value, s =>
                    {
                        s.Phase = "LlmReady";
                        s.NeuralCompleted = true;
                        s.StepIndex = s.FeaturesCompleted ? 3 : 2;
                        s.LlmScore = llmOutcome.Succeeded ? llmOutcome.AlignmentScore : null;
                        s.LlmSummaryPreview = llmOutcome.Succeeded
                            ? TruncateForJob(llmOutcome.Summary)
                            : TruncateForJob(llmOutcome.ErrorMessage);
                        s.Message = llmOutcome.Succeeded
                            ? s.FeaturesCompleted
                                ? "Нейросетевой и признаковый анализ завершены. Собираем итоговый результат…"
                                : "Нейросетевой анализ завершён. Выполняется признаковый анализ…"
                            : "Нейросетевой этап завершился с ошибкой; итог будет по признаковому анализу.";
                    });
                }
            }
        }

        pythonOutcome ??= await pythonTask;
        llmOutcome ??= await llmTask;
        heuristicScore ??= Math.Clamp(
            CalculateScore(weights, lexical, pythonOutcome.Fragments, matches),
            0,
            100);

        var fragments = DeduplicateIdenticalSpans(
            CapFragments(
                lexicalFragments
                    .Concat(MapPythonFragments(pythonOutcome.Fragments))
                    .ToList()));

        var combinedScore = CombineHeuristicAndLlm(heuristicScore.Value, llmOutcome, runSettings);
        var submission = await GetOrCreateNewsSubmissionAsync(input, cancellationToken);

        var usedPublications = SelectPublicationsUsedInVerification(pubs, matches, corpusForLlm);
        var persisted = await relevantCorpusService.PersistUsedAsync(usedPublications, cancellationToken);
        matches = ApplyPersistedIds(matches, persisted);

        var record = BuildAnalysisRecord(
            submission,
            heuristicScore.Value,
            llmOutcome,
            combinedScore,
            fragments,
            matches,
            lexical,
            pythonOutcome,
            runSettings);

        if (jobId.HasValue)
        {
            jobStore.Patch(jobId.Value, s =>
            {
                s.Phase = "Combining";
                s.FeaturesCompleted = true;
                s.NeuralCompleted = true;
                s.StepIndex = 3;
                s.CombinedScore = combinedScore;
                s.Message = "Сохранение результата…";
            });
        }

        db.AnalysisRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return (record, matches);
    }

    private async Task<NewsSubmission> GetOrCreateNewsSubmissionAsync(
        AnalyzeNewsInputModel input,
        CancellationToken cancellationToken)
    {
        var fingerprint = NewsContentFingerprint.Compute(input.Headline, input.NewsText);
        var existing = await db.NewsSubmissions
            .FirstOrDefaultAsync(s => s.ContentFingerprint == fingerprint, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var submission = new NewsSubmission
        {
            Headline = input.Headline.Trim(),
            NewsText = input.NewsText.Trim(),
            SourceUrl = string.IsNullOrWhiteSpace(input.SourceUrl) ? null : input.SourceUrl.Trim(),
            ContentFingerprint = fingerprint
        };
        db.NewsSubmissions.Add(submission);
        return submission;
    }

    private static AnalysisRecord BuildAnalysisRecord(
        NewsSubmission submission,
        int heuristicScore,
        OllamaComparisonOutcome llmOutcome,
        int combinedScore,
        List<SuspiciousFragment> fragments,
        List<OfficialPublicationMatchVm> matches,
        LexicalFeatures lexical,
        PythonAnalysisOutcome pythonOutcome,
        EffectiveAnalysisRunSettings runSettings)
    {
        return new AnalysisRecord
        {
            NewsSubmission = submission,
            HeuristicReliabilityScore = heuristicScore,
            LlmAlignmentScore = llmOutcome.Succeeded ? llmOutcome.AlignmentScore : null,
            LlmSummary = BuildLlmSummaryLine(llmOutcome),
            ReliabilityScore = combinedScore,
            Explanation = BuildExplanation(
                combinedScore,
                heuristicScore,
                llmOutcome,
                fragments.Count,
                matches.Count,
                lexical,
                pythonOutcome,
                runSettings),
            SuspiciousFragments = fragments,
            OfficialPublicationMatches = matches.Select(match => new OfficialPublicationMatch
            {
                OfficialPublicationId = match.OfficialPublicationId,
                RelevanceScore = match.RelevanceScore,
                MatchedAtUtc = DateTime.UtcNow
            }).ToList()
        };
    }

    private static string? TruncateForJob(string? text, int max = 280)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private int CombineHeuristicAndLlm(
        int heuristicScore,
        OllamaComparisonOutcome llmOutcome,
        EffectiveAnalysisRunSettings runSettings)
    {
        if (!llmOutcome.WasAttempted || !llmOutcome.Succeeded || !llmOutcome.AlignmentScore.HasValue)
        {
            return heuristicScore;
        }

        var wh = runSettings.HeuristicBlendWeight;
        var wl = runSettings.LlmBlendWeight;
        var sum = wh + wl;
        if (sum <= 0)
        {
            wh = 1;
            wl = 0;
            sum = 1;
        }

        wh /= sum;
        wl /= sum;

        var combined = (int)Math.Round(wh * heuristicScore + wl * llmOutcome.AlignmentScore!.Value);
        return Math.Clamp(combined, 0, 100);
    }

    private static string? BuildLlmSummaryLine(OllamaComparisonOutcome llm)
    {
        if (!llm.WasAttempted)
        {
            return string.IsNullOrWhiteSpace(llm.ErrorMessage)
                ? null
                : $"Ollama: {llm.ErrorMessage}";
        }

        if (llm.Succeeded)
        {
            if (string.IsNullOrWhiteSpace(llm.Summary))
            {
                return llm.AlignmentScore.HasValue
                    ? $"Предварительная оценка модели (без текстового резюме): {llm.AlignmentScore} из 100. Рекомендуется сверка с официальными источниками."
                    : "Модель завершила анализ без текстового резюме; ориентируйтесь на эвристическую оценку.";
            }

            var s = llm.Summary.Trim();
            return s.Length > 8000 ? s[..7997] + "…" : s;
        }

        return string.IsNullOrWhiteSpace(llm.ErrorMessage) ? "Ollama: ошибка без сообщения." : $"Ollama: {llm.ErrorMessage}";
    }

    private async Task<List<OfficialPublication>> LoadPublicationsCorpusAsync(
        string headline,
        string newsText,
        EffectiveAnalysisRunSettings? runSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            var relevant = await relevantCorpusService.FetchRelevantAsync(
                headline,
                newsText,
                runSettings,
                cancellationToken);
            if (relevant.Count > 0)
            {
                logger.LogInformation(
                    "Corpus for analysis: {Count} relevant publication(s) from parsers and matching manual entries",
                    relevant.Count);
                return relevant;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Relevant corpus fetch canceled internally");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch relevant publications for analysis");
        }

        logger.LogWarning("Corpus for analysis is empty: no relevant publications found for this news text");
        return [];
    }

    private static List<OfficialPublication> SelectPublicationsUsedInVerification(
        List<OfficialPublication> corpus,
        List<OfficialPublicationMatchVm> matches,
        List<OfficialPublication> corpusForLlm)
    {
        var usedUrls = matches
            .Select(m => HtmlTextExtractor.NormalizeUrl(m.Url))
            .Concat(corpusForLlm.Select(p => HtmlTextExtractor.NormalizeUrl(p.Url)))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return corpus
            .Where(p => usedUrls.Contains(HtmlTextExtractor.NormalizeUrl(p.Url)))
            .ToList();
    }

    private static List<OfficialPublicationMatchVm> ApplyPersistedIds(
        List<OfficialPublicationMatchVm> matches,
        List<OfficialPublication> persisted)
    {
        var byUrl = persisted.ToDictionary(
            p => HtmlTextExtractor.NormalizeUrl(p.Url),
            p => p,
            StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches)
        {
            if (byUrl.TryGetValue(HtmlTextExtractor.NormalizeUrl(match.Url), out var pub))
            {
                match.OfficialPublicationId = pub.Id;
                match.SourceName = pub.TrustedSource?.Name ?? pub.SourceName;
            }
        }

        return matches;
    }

    private async Task<List<OfficialPublicationMatchVm>> GetStoredMatchesAsync(
        int analysisRecordId,
        CancellationToken cancellationToken)
    {
        var rows = await db.OfficialPublicationMatches
            .AsNoTracking()
            .Include(m => m.OfficialPublication!)
            .ThenInclude(p => p.TrustedSource)
            .Where(m => m.AnalysisRecordId == analysisRecordId)
            .OrderByDescending(m => m.RelevanceScore)
            .ToListAsync(cancellationToken);

        return rows.Select(m => new OfficialPublicationMatchVm
        {
            OfficialPublicationId = m.OfficialPublicationId,
            SourceName = m.OfficialPublication!.TrustedSource!.Name,
            Title = m.OfficialPublication.Title,
            Url = m.OfficialPublication.Url,
            RelevanceScore = m.RelevanceScore,
            HasStatistics = RelevanceScoring.CandidateHasStatistics($"{m.OfficialPublication.Title} {m.OfficialPublication.Content}")
        }).ToList();
    }

    private static List<OfficialPublication> SelectCorpusForLlm(
        string headline,
        string newsText,
        List<OfficialPublication> pubs,
        int maxSnippets)
    {
        if (pubs.Count == 0)
        {
            return [];
        }

        var ordered = pubs
            .Select(o => (o, Score: CalculateRelevance(headline, newsText, o.Content)))
            .OrderByDescending(x => x.Score)
            .ToList();

        var picked = ordered.Where(x => x.Score > 0).Select(x => x.o).Take(maxSnippets).ToList();
        if (picked.Count > 0)
        {
            return picked;
        }

        return ordered.Select(x => x.o).Take(maxSnippets).ToList();
    }

    private static List<OfficialPublicationMatchVm> RankMatchesToViewModels(
        string headline,
        string newsText,
        List<OfficialPublication> pubs)
    {
        var minScore = RelevanceScoring.QueryExpectsStatistics(headline, newsText) ? 22 : 18;
        return pubs
            .Select(o => new OfficialPublicationMatchVm
            {
                OfficialPublicationId = o.Id,
                SourceName = o.SourceName,
                Title = o.Title,
                Url = o.Url,
                RelevanceScore = CalculateRelevance(headline, newsText, o.Content),
                HasStatistics = RelevanceScoring.CandidateHasStatistics($"{o.Title} {o.Content}")
            })
            .Where(m => m.RelevanceScore >= minScore)
            .OrderByDescending(m => m.RelevanceScore)
            .Take(5)
            .ToList();
    }

    private static List<SuspiciousFragment> DeduplicateIdenticalSpans(List<SuspiciousFragment> fragments)
    {
        var withSpan = fragments.Where(f => f.StartOffset >= 0 && f.EndOffset > f.StartOffset).ToList();
        var noSpan = fragments.Where(f => f.StartOffset < 0 || f.EndOffset <= f.StartOffset).ToList();
        var merged = withSpan
            .GroupBy(f => (f.StartOffset, f.EndOffset))
            .Select(g => g.OrderBy(FragmentPriority).ThenByDescending(f => f.Severity).First())
            .ToList();
        merged.AddRange(noSpan);
        return merged;
    }

    private static List<SuspiciousFragment> MapPythonFragments(IReadOnlyList<PythonFragmentResult> pythonFragments)
    {
        var list = new List<SuspiciousFragment>();
        foreach (var f in pythonFragments)
        {
            var kind = MapPythonFeatureKind(f.Kind);
            var start = f.Start ?? -1;
            var end = f.End ?? -1;
            if (start >= 0 && end > start)
            {
                list.Add(new SuspiciousFragment
                {
                    FragmentText = f.Fragment,
                    Reason = f.Reason,
                    Severity = f.Severity,
                    FeatureKind = kind,
                    StartOffset = start,
                    EndOffset = end
                });
            }
            else
            {
                list.Add(new SuspiciousFragment
                {
                    FragmentText = f.Fragment,
                    Reason = f.Reason,
                    Severity = f.Severity,
                    FeatureKind = kind,
                    StartOffset = -1,
                    EndOffset = -1
                });
            }
        }

        return list;
    }

    private static SuspiciousFeatureKind MapPythonFeatureKind(string? kind) =>
        kind?.Trim().ToLowerInvariant() switch
        {
            "emotional" => SuspiciousFeatureKind.Emotional,
            "evaluative" => SuspiciousFeatureKind.Evaluative,
            "manipulative" => SuspiciousFeatureKind.Manipulative,
            "python" => SuspiciousFeatureKind.PythonHeuristic,
            _ => SuspiciousFeatureKind.PythonHeuristic
        };

    private static List<SuspiciousFragment> CapFragments(List<SuspiciousFragment> fragments, int max = 220) =>
        fragments
            .OrderBy(FragmentPriority)
            .ThenBy(f => f.StartOffset < 0 ? int.MaxValue : f.StartOffset)
            .Take(max)
            .ToList();

    private static int FragmentPriority(SuspiciousFragment f) => f.FeatureKind switch
    {
        SuspiciousFeatureKind.Manipulative => 0,
        SuspiciousFeatureKind.PythonHeuristic => 1,
        SuspiciousFeatureKind.Emotional => 2,
        SuspiciousFeatureKind.Evaluative => 3,
        SuspiciousFeatureKind.UppercaseWord => 4,
        SuspiciousFeatureKind.Exclamation => 5,
        SuspiciousFeatureKind.Question => 6,
        SuspiciousFeatureKind.Link => 7,
        SuspiciousFeatureKind.Date => 8,
        SuspiciousFeatureKind.Number => 9,
        SuspiciousFeatureKind.SourceCue => 10,
        _ => 20
    };

    private async Task<List<OfficialPublicationMatchVm>> GetOfficialMatchesInternalAsync(
        string headline,
        string text,
        CancellationToken cancellationToken)
    {
        var pubs = await LoadPublicationsCorpusAsync(
            headline,
            text,
            analysisDefaultsService.GetDefaults(),
            cancellationToken);
        return RankMatchesToViewModels(headline, text, pubs);
    }

    private static int CalculateRelevance(string headline, string newsText, string officialContent) =>
        RelevanceScoring.CalculateMatchScore(headline, newsText, officialContent);

    private static string BuildExplanation(
        int combinedScore,
        int heuristicScore,
        OllamaComparisonOutcome llmOutcome,
        int suspiciousCount,
        int matchCount,
        LexicalFeatures lexical,
        PythonAnalysisOutcome pythonOutcome,
        EffectiveAnalysisRunSettings runSettings)
    {
        var lines = new List<string>
        {
            $"Итоговая оценка достоверности (комбинированная): {combinedScore} из 100.",
            $"Оценка по признакам: {heuristicScore} из 100.",
            BuildLlmExplanationLine(llmOutcome),
            $"Выделено фрагментов в тексте (маркеров): {suspiciousCount}.",
            $"Релевантных официальных публикаций по ключевым словам: {matchCount}.",
            matchCount > 0
                ? "Сопоставление с официальными материалами: есть пересечения по тексту."
                : "Сопоставление с официальными материалами: релевантных совпадений по выбранным источникам не найдено.",
            "Оценка нейросети (Ollama) носит вспомогательный характер и не заменяет экспертизу врача или официальных рекомендаций.",
            runSettings.ToSummaryLine(),
            "Счётчики эмоциональной, манипулятивной и оценочной лексики ниже — по правилам приложения (C#); они не зависят от дополнительного модуля Python.",
            $"Эмоциональная лексика (словарь, C#): {lexical.EmotionalHits}; манипулятивные фразы (C#): {lexical.ManipulativeHits}; оценочная лексика (C#): {lexical.EvaluativeHits}.",
            $"Верхний регистр (длинные токены): {lexical.UppercaseWords}; восклицательные знаки: {lexical.ExclamationCount}; вопросительные: {lexical.QuestionCount}.",
            $"Даты (цифровые и словесные): {(lexical.HasDates ? "есть" : "нет")}{(lexical.RussianDateHits > 0 ? $", из них словесных формулировок: {lexical.RussianDateHits}" : string.Empty)}.",
            $"Ссылки в тексте: {(lexical.HasLinks ? "есть" : "нет")}; числа: {(lexical.HasNumbers ? "есть" : "нет")}; явные отсылки к источнику: {(lexical.HasSourceCue ? "есть" : "нет")}.",
            BuildPythonExplanationLine(pythonOutcome)
        };

        var explanation = string.Join('\n', lines);
        // Truncate to 8000 characters to fit the database constraint
        return explanation.Length > 8000 ? explanation[..7997] + "…" : explanation;
    }

    private static string BuildLlmExplanationLine(OllamaComparisonOutcome llm)
    {
        if (!llm.WasAttempted)
        {
            return "Нейросеть (Ollama): отключена в настройках (Ollama:Enabled). Итоговая оценка совпадает с признаковым анализом.";
        }

        if (llm.Succeeded && llm.AlignmentScore.HasValue)
        {
            string tail;
            if (string.IsNullOrWhiteSpace(llm.Summary))
            {
                tail = string.Empty;
            }
            else
            {
                var summary = llm.Summary.Trim();
                // Limit summary to 1500 characters in explanation to leave room for other content
                if (summary.Length > 1500)
                {
                    summary = summary[..1497] + "…";
                }
                tail = $" Кратко: {summary}";
            }
            return $"Нейросеть (Ollama): согласованность с выдержками корпуса — {llm.AlignmentScore} из 100.{tail}";
        }

        var err = string.IsNullOrWhiteSpace(llm.ErrorMessage) ? "см. логи приложения" : llm.ErrorMessage.Trim();
        return $"Нейросеть (Ollama): не удалось получить оценку ({err}). Итог рассчитан по признаковому анализу.";
    }

    private static string BuildPythonExplanationLine(PythonAnalysisOutcome o)
    {
        if (o.Status != PythonAnalysisStatus.Ok)
        {
            return o.Status switch
            {
                PythonAnalysisStatus.ScriptMissing =>
                    "Дополнительный модуль (Python): скрипт не найден по настройке Python:ScriptPath; проверьте путь и логи приложения.",
                PythonAnalysisStatus.StartFailed =>
                    "Дополнительный модуль (Python): не удалось запустить интерпретатор (Python:ExecutablePath); проверьте PATH и логи.",
                PythonAnalysisStatus.NonZeroExit =>
                    $"Дополнительный модуль (Python): процесс завершился с ошибкой (код выхода {o.ExitCode}); см. логи приложения.",
                PythonAnalysisStatus.Timeout =>
                    "Дополнительный модуль (Python): превышен таймаут (Python:TimeoutSeconds); при первом запуске тяжёлых моделей увеличьте таймаут или отключите Natasha/Stanza.",
                PythonAnalysisStatus.JsonError =>
                    "Дополнительный модуль (Python): ответ не удалось разобрать как JSON; см. логи приложения (возможен сбой скрипта или лишний вывод в stdout).",
                _ => "Дополнительный модуль (Python): не выполнен; см. логи приложения."
            };
        }

        var n = o.Fragments.Count;
        if (n > 0)
        {
            return $"Дополнительный модуль (Python): возвращено {n} фрагментов (словари RU/EN и правила в скрипте).";
        }

        return "Дополнительный модуль (Python): выполнен успешно, дополнительных фрагментов не вернул; маркеры в тексте при этом могут полностью относиться к анализу приложения (C#).";
    }

    private static string Normalize(string text) => NewsContentFingerprint.Normalize(text);

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinksRegex();

    [GeneratedRegex(@"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b|\b(?:19|20)\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DatesRegex();

    [GeneratedRegex(@"\d")]
    private static partial Regex AnyDigitRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}\s-]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"[\p{L}\d][\p{L}\d-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordTokenRegex();

    [GeneratedRegex(@"!+")]
    private static partial Regex ExclamationRunRegex();

    [GeneratedRegex(@"\?+")]
    private static partial Regex QuestionRunRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?(?:\s*%)?")]
    private static partial Regex NumberTokenRegex();

    [GeneratedRegex(
        """с\s+\d{1,2}\s+по\s+\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)(?:\s+\d{4}\s*(?:года|г\.))?|(?:с\s+начала|до\s+конца|в\s+начале|в\s+конце)\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\s+\d{4}\s*(?:года|г\.)?|\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\s+\d{4}\s*(?:года|г\.)?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RussianNaturalDatesRegex();

    private (LexicalFeatures lexical, List<SuspiciousFragment> fragments) AnalyzeLexicalWithSpans(string text, Lexicons lexicons)
    {
        var sentences = text
            .Split(['.', '!', '?', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var fragments = new List<SuspiciousFragment>();

        var tokens = new List<string>();
        foreach (Match m in WordTokenRegex().Matches(text))
        {
            var cleaned = CleanToken(m.Value);
            if (cleaned.Length > 1)
            {
                tokens.Add(cleaned);
            }
        }

        var tokenCount = Math.Max(1, tokens.Count);

        var emotionalHits = 0;
        var evaluativeHits = 0;

        foreach (Match m in WordTokenRegex().Matches(text))
        {
            var cleaned = CleanToken(m.Value);
            if (cleaned.Length <= 1)
            {
                continue;
            }

            var lemma = Lemmatize(cleaned);
            if (lexicons.Emotional.Contains(lemma))
            {
                emotionalHits++;
                fragments.Add(new SuspiciousFragment
                {
                    FeatureKind = SuspiciousFeatureKind.Emotional,
                    StartOffset = m.Index,
                    EndOffset = m.Index + m.Length,
                    FragmentText = m.Value,
                    Reason = "Совпадение со словарём эмоционально окрашенной лексики.",
                    Severity = 3
                });
            }

            if (lexicons.Evaluative.Contains(lemma))
            {
                evaluativeHits++;
                fragments.Add(new SuspiciousFragment
                {
                    FeatureKind = SuspiciousFeatureKind.Evaluative,
                    StartOffset = m.Index,
                    EndOffset = m.Index + m.Length,
                    FragmentText = m.Value,
                    Reason = "Совпадение со словарём оценочной лексики.",
                    Severity = 2
                });
            }
        }

        var manipulativeHits = 0;
        foreach (var phrase in lexicons.Manipulative)
        {
            if (string.IsNullOrWhiteSpace(phrase))
            {
                continue;
            }

            foreach (var (start, end) in FindAllOccurrences(text, phrase))
            {
                manipulativeHits++;
                fragments.Add(new SuspiciousFragment
                {
                    FeatureKind = SuspiciousFeatureKind.Manipulative,
                    StartOffset = start,
                    EndOffset = end,
                    FragmentText = text[start..end],
                    Reason = "Фраза из словаря манипулятивных конструкций.",
                    Severity = 8
                });
            }
        }

        foreach (var phrase in lexicons.SourceCues)
        {
            if (string.IsNullOrWhiteSpace(phrase))
            {
                continue;
            }

            foreach (var (start, end) in FindAllOccurrencesWordBounded(text, phrase))
            {
                fragments.Add(new SuspiciousFragment
                {
                    FeatureKind = SuspiciousFeatureKind.SourceCue,
                    StartOffset = start,
                    EndOffset = end,
                    FragmentText = text[start..end],
                    Reason = "Указание на источник или ссылку на документ в тексте.",
                    Severity = 0
                });
            }
        }

        var sourceCueHits = fragments.Any(f => f.FeatureKind == SuspiciousFeatureKind.SourceCue);

        var uppercaseWords = 0;
        foreach (Match m in WordTokenRegex().Matches(text))
        {
            if (m.Length < 4)
            {
                continue;
            }

            var val = m.Value;
            if (!val.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
            {
                continue;
            }

            if (!val.Any(char.IsLetter))
            {
                continue;
            }

            uppercaseWords++;
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.UppercaseWord,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = val,
                Reason = "Слово или токен полностью в верхнем регистре.",
                Severity = 4
            });
        }

        foreach (Match m in ExclamationRunRegex().Matches(text))
        {
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.Exclamation,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = m.Value,
                Reason = m.Length > 1
                    ? "Повторяющиеся восклицательные знаки."
                    : "Восклицательный знак.",
                Severity = Math.Min(6, 2 * m.Length)
            });
        }

        foreach (Match m in QuestionRunRegex().Matches(text))
        {
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.Question,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = m.Value,
                Reason = m.Length > 1
                    ? "Повторяющиеся вопросительные знаки."
                    : "Вопросительный знак.",
                Severity = Math.Min(4, m.Length)
            });
        }

        foreach (Match m in LinksRegex().Matches(text))
        {
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.Link,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = m.Value,
                Reason = "Обнаружена гиперссылка.",
                Severity = 0
            });
        }

        foreach (Match m in DatesRegex().Matches(text))
        {
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.Date,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = m.Value,
                Reason = "Обнаружена дата в цифровом или смешанном формате.",
                Severity = 0
            });
        }

        foreach (Match m in RussianNaturalDatesRegex().Matches(text))
        {
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.Date,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = m.Value,
                Reason = "Дата в словесной формулировке (например, «12 мая 2026 года», «с 1 по 30 апреля»).",
                Severity = 0
            });
        }

        foreach (Match m in NumberTokenRegex().Matches(text))
        {
            fragments.Add(new SuspiciousFragment
            {
                FeatureKind = SuspiciousFeatureKind.Number,
                StartOffset = m.Index,
                EndOffset = m.Index + m.Length,
                FragmentText = m.Value,
                Reason = "Числовое значение или процент.",
                Severity = 0
            });
        }

        var exclamationCount = text.Count(c => c == '!');
        var questionCount = text.Count(c => c == '?');
        var hasLinks = LinksRegex().IsMatch(text);
        var russianDateHits = RussianNaturalDatesRegex().Matches(text).Count;
        var hasDates = DatesRegex().IsMatch(text) || russianDateHits > 0;
        var hasNumbers = AnyDigitRegex().IsMatch(text);

        var lexical = new LexicalFeatures(
            sentences.Count,
            tokenCount,
            emotionalHits,
            manipulativeHits,
            evaluativeHits,
            sourceCueHits,
            uppercaseWords,
            exclamationCount,
            questionCount,
            hasLinks,
            hasDates,
            hasNumbers,
            russianDateHits);

        return (lexical, fragments);
    }

    private static IEnumerable<(int start, int end)> FindAllOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            yield break;
        }

        var n = needle.Trim();
        if (n.Length == 0)
        {
            yield break;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        var pos = 0;
        while (pos < haystack.Length)
        {
            var idx = haystack.IndexOf(n, pos, comparison);
            if (idx < 0)
            {
                yield break;
            }

            yield return (idx, idx + n.Length);
            pos = idx + Math.Max(1, n.Length);
        }
    }

    /// <summary>
    /// Совпадение только как целое «слово»: не внутри более длинного токена (например «воз» в «возможно»).
    /// </summary>
    private static IEnumerable<(int start, int end)> FindAllOccurrencesWordBounded(string haystack, string needle)
    {
        var n = needle.Trim();
        if (n.Length == 0)
        {
            yield break;
        }

        var escaped = Regex.Escape(n);
        var pattern = $@"(?<!\p{{L}}){escaped}(?!\p{{L}})";
        var rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match m in rx.Matches(haystack))
        {
            if (m.Success)
            {
                yield return (m.Index, m.Index + m.Length);
            }
        }
    }

    private static string CleanToken(string token)
    {
        var cleaned = NonWordRegex().Replace(token.ToLowerInvariant(), "");
        return cleaned.Trim();
    }

    // Упрощённая нормализация словоформ для сопоставления со словарями (RU).
    private static string Lemmatize(string token)
    {
        if (token.Length <= 4)
        {
            return token;
        }

        var endings = new[]
        {
            "иями", "ями", "ами", "ого", "ему", "ыми", "ими", "ий", "ый", "ая", "ое", "ие", "ые", "ов", "ев", "ам", "ям", "ах", "ях", "ом", "ем", "ой", "ей", "ую", "юю", "ия", "ья", "а", "я", "ы", "и", "у", "ю", "о", "е"
        };

        foreach (var ending in endings.OrderByDescending(e => e.Length))
        {
            if (token.EndsWith(ending, StringComparison.Ordinal))
            {
                return token[..^ending.Length];
            }
        }

        return token;
    }

    private static int CalculateScore(
        FeatureWeights w,
        LexicalFeatures lexical,
        IReadOnlyList<PythonFragmentResult> pythonFragments,
        List<OfficialPublicationMatchVm> matches)
    {
        var score = w.BaseScore;
        score -= (int)Math.Round(w.EmotionalDensityPenalty * lexical.EmotionalDensity);
        score -= (int)Math.Round(w.ManipulativeDensityPenalty * lexical.ManipulativeDensity);
        score -= (int)Math.Round(w.EvaluativeDensityPenalty * lexical.EvaluativeDensity);
        score -= (int)Math.Round(w.UppercaseRatioPenalty * lexical.UppercaseRatio);
        score -= Math.Min(lexical.ExclamationCount * w.ExclamationPenalty, 20);
        score -= Math.Min(lexical.QuestionCount * w.QuestionPenalty, 10);

        if (lexical.HasLinks)
        {
            score += w.HasLinksBonus;
        }

        if (lexical.HasDates)
        {
            score += w.HasDatesBonus;
        }

        if (lexical.RussianDateHits > 0)
        {
            score += Math.Min(2, lexical.RussianDateHits * w.RussianDateExtraBonusPerHit);
        }

        if (lexical.HasNumbers)
        {
            score += w.HasNumbersBonus;
        }

        if (lexical.HasSourceCue)
        {
            score += w.HasSourceCueBonus;
        }

        score -= Math.Min(w.PythonPenaltyCap, pythonFragments.Sum(f => f.Severity));
        score += Math.Min(w.SourcesBonusCap, matches.Sum(m => m.RelevanceScore) / 5);

        // Много манипулятивных маркеров при отсутствии пересечений с официальными материалами — типичный профиль дезинформации.
        if (matches.Count == 0 && lexical.ManipulativeHits >= 8)
        {
            score -= 6;
        }

        return score;
    }

    private static readonly ConcurrentDictionary<string, Lexicons> LexiconCache = new();
    private static readonly ConcurrentDictionary<string, FeatureWeights> WeightCache = new();

    private Lexicons LoadLexicons()
    {
        var basePath = configuration["AnalysisLexicons:BasePath"] ?? "Resources/Lexicons";
        var root = Path.Combine(env.ContentRootPath, basePath);
        var emotionalFile = configuration["AnalysisLexicons:EmotionalFile"] ?? "emotional_ru.txt";
        var manipulativeFile = configuration["AnalysisLexicons:ManipulativeFile"] ?? "manipulative_ru.txt";
        var evaluativeFile = configuration["AnalysisLexicons:EvaluativeFile"] ?? "evaluative_ru.txt";
        var sourceCuesFile = configuration["AnalysisLexicons:SourceCuesFile"] ?? "source_cues_ru.txt";

        var cacheKey = string.Join('|', root, emotionalFile, manipulativeFile, evaluativeFile, sourceCuesFile);
        return LexiconCache.GetOrAdd(cacheKey, _ => new Lexicons(
            LoadEmotionalLexicons(root, emotionalFile),
            ReadLexicon(Path.Combine(root, manipulativeFile)),
            ReadLexicon(Path.Combine(root, evaluativeFile)),
            ReadLexicon(Path.Combine(root, sourceCuesFile))));
    }

    /// <summary>
    /// Основной эмоциональный словарь + опциональный emotional_ru_rusentilex.txt (см. python/tools/build_lexicons.py).
    /// </summary>
    private static HashSet<string> LoadEmotionalLexicons(string root, string mainName)
    {
        var merged = ReadLexicon(Path.Combine(root, mainName));
        var extraPath = Path.Combine(root, "emotional_ru_rusentilex.txt");
        if (File.Exists(extraPath))
        {
            foreach (var w in ReadLexicon(extraPath))
            {
                merged.Add(w);
            }
        }

        return merged;
    }

    private FeatureWeights LoadWeights()
    {
        var relativePath = configuration["AnalysisScoring:WeightsFile"] ?? "Resources/Scoring/feature_weights.json";
        var absolutePath = Path.Combine(env.ContentRootPath, relativePath);
        return WeightCache.GetOrAdd(absolutePath, CreateWeights);
    }

    private static FeatureWeights CreateWeights(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return FeatureWeights.Default;
        }

        var json = File.ReadAllText(absolutePath);
        return JsonSerializer.Deserialize<FeatureWeights>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? FeatureWeights.Default;
    }

    private static HashSet<string> ReadLexicon(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path)
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
            .ToHashSet();
    }

    private sealed record Lexicons(
        HashSet<string> Emotional,
        HashSet<string> Manipulative,
        HashSet<string> Evaluative,
        HashSet<string> SourceCues);

    private sealed record LexicalFeatures(
        int SentenceCount,
        int TokenCount,
        int EmotionalHits,
        int ManipulativeHits,
        int EvaluativeHits,
        bool HasSourceCue,
        int UppercaseWords,
        int ExclamationCount,
        int QuestionCount,
        bool HasLinks,
        bool HasDates,
        bool HasNumbers,
        int RussianDateHits)
    {
        public double EmotionalDensity => (double)EmotionalHits / TokenCount;
        public double ManipulativeDensity => (double)ManipulativeHits / Math.Max(1, SentenceCount);
        public double EvaluativeDensity => (double)EvaluativeHits / TokenCount;
        public double UppercaseRatio => (double)UppercaseWords / TokenCount;
    }

    private sealed class FeatureWeights
    {
        public int BaseScore { get; set; } = 40;
        public int EmotionalDensityPenalty { get; set; } = 22;
        public int ManipulativeDensityPenalty { get; set; } = 36;
        public int EvaluativeDensityPenalty { get; set; } = 10;
        public int UppercaseRatioPenalty { get; set; } = 20;
        public int ExclamationPenalty { get; set; } = 2;
        public int QuestionPenalty { get; set; } = 1;
        public int HasLinksBonus { get; set; } = 2;
        public int HasDatesBonus { get; set; } = 2;
        public int HasNumbersBonus { get; set; } = 2;
        public int HasSourceCueBonus { get; set; } = 2;
        public int RussianDateExtraBonusPerHit { get; set; } = 1;
        public int PythonPenaltyCap { get; set; } = 42;
        public int SourcesBonusCap { get; set; } = 35;

        public static FeatureWeights Default => new();
    }
}
