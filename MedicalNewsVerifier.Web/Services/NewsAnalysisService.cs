using System.Text.Json;
using System.Text.RegularExpressions;
using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Services;

public partial class NewsAnalysisService(
    AppDbContext db,
    IPythonLinguisticClient pythonClient,
    IOfficialSourceFetcher officialSourceFetcher,
    IOllamaComparisonClient ollamaClient,
    IAnalysisJobStore jobStore,
    IWebHostEnvironment env,
    IConfiguration configuration,
    ILogger<NewsAnalysisService> logger) : INewsAnalysisService
{
    public async Task<AnalysisRecord?> GetAnalysisByIdAsync(int id, CancellationToken cancellationToken) =>
        await db.AnalysisRecords
            .Include(r => r.SuspiciousFragments)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<List<OfficialPublicationMatchVm>> GetOfficialMatchesAsync(string newsText, CancellationToken cancellationToken) =>
        await GetOfficialMatchesInternalAsync(newsText, cancellationToken);

    public async Task<(AnalysisRecord record, List<OfficialPublicationMatchVm> matches, bool isFromHistory)> AnalyzeAndSaveAsync(
        AnalyzeNewsInputModel input,
        bool forceNew,
        CancellationToken cancellationToken)
    {
        var normalizedHeadline = Normalize(input.Headline);
        var normalizedText = Normalize(input.NewsText);
        var recentRecords = await db.AnalysisRecords
            .Include(r => r.SuspiciousFragments)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        var existing = recentRecords.FirstOrDefault(r =>
            Normalize(r.Headline) == normalizedHeadline &&
            Normalize(r.NewsText) == normalizedText);

        if (!forceNew && existing is not null)
        {
            var historyMatches = await GetOfficialMatchesInternalAsync(input.NewsText, cancellationToken);
            return (existing, historyMatches, true);
        }

        var doc = AnalyzedDocument.From(input.Headline, input.NewsText);
        var fullText = doc.FullText;
        var lexicons = LoadLexicons();
        var weights = LoadWeights();

        var publications = await LoadPublicationsCorpusAsync(cancellationToken);
        var matches = RankMatchesToViewModels(input.NewsText, publications);
        var corpusForLlm = SelectCorpusForLlm(input.NewsText, publications, configuration.GetValue("Ollama:MaxCorpusSnippets", 4));

        var pythonTask = pythonClient.AnalyzeAsync(fullText, cancellationToken);
        var llmTask = ollamaClient.CompareNewsToCorpusAsync(input.Headline, input.NewsText, corpusForLlm, cancellationToken);
        await Task.WhenAll(pythonTask, llmTask);

        var pythonOutcome = await pythonTask;
        var llmOutcome = await llmTask;

        var (lexical, lexicalFragments) = AnalyzeLexicalWithSpans(fullText, lexicons);
        var pythonFragments = pythonOutcome.Fragments;

        var fragments = new List<SuspiciousFragment>();
        fragments.AddRange(lexicalFragments);
        fragments.AddRange(MapPythonFragments(pythonFragments));
        fragments = DeduplicateIdenticalSpans(fragments);
        fragments = CapFragments(fragments);

        var heuristicScore = CalculateScore(weights, lexical, pythonFragments, matches);
        heuristicScore = Math.Clamp(heuristicScore, 0, 100);

        var (combinedScore, status) = CombineHeuristicAndLlm(heuristicScore, llmOutcome);

        var record = new AnalysisRecord
        {
            Headline = input.Headline,
            NewsText = input.NewsText,
            SourceUrl = input.SourceUrl,
            HeuristicReliabilityScore = heuristicScore,
            LlmAlignmentScore = llmOutcome.Succeeded ? llmOutcome.AlignmentScore : null,
            LlmSummary = BuildLlmSummaryLine(llmOutcome),
            ReliabilityScore = combinedScore,
            Status = status,
            Explanation = BuildExplanation(
                combinedScore,
                heuristicScore,
                llmOutcome,
                fragments.Count,
                matches.Count,
                lexical,
                pythonOutcome),
            SuspiciousFragments = fragments
        };

        db.AnalysisRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return (record, matches, false);
    }

    public async Task RunAnalysisJobAsync(Guid jobId, AnalyzeNewsInputModel input, CancellationToken cancellationToken)
    {
        try
        {
            jobStore.Patch(jobId, s =>
            {
                s.Phase = "LoadingSources";
                s.Message = "Загрузка корпуса доверенных материалов и источников…";
            });

            var normalizedHeadline = Normalize(input.Headline);
            var normalizedText = Normalize(input.NewsText);
            var recentRecords = await db.AnalysisRecords
                .Include(r => r.SuspiciousFragments)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(200)
                .ToListAsync(cancellationToken);

            var existing = recentRecords.FirstOrDefault(r =>
                Normalize(r.Headline) == normalizedHeadline &&
                Normalize(r.NewsText) == normalizedText);

            if (existing is not null)
            {
                var historyMatches = await GetOfficialMatchesInternalAsync(input.NewsText, cancellationToken);
                jobStore.Patch(jobId, s =>
                {
                    s.Phase = "Completed";
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

            var doc = AnalyzedDocument.From(input.Headline, input.NewsText);
            var fullText = doc.FullText;
            var lexicons = LoadLexicons();
            var weights = LoadWeights();

            var publications = await LoadPublicationsCorpusAsync(cancellationToken);
            jobStore.Patch(jobId, s =>
            {
                s.Phase = "RunningAnalyzers";
                s.Message = "Параллельно выполняются эвристический модуль (Python) и сравнение с корпусом через Ollama…";
            });

            var matches = RankMatchesToViewModels(input.NewsText, publications);
            var corpusForLlm = SelectCorpusForLlm(input.NewsText, publications, configuration.GetValue("Ollama:MaxCorpusSnippets", 4));

            async Task<PythonAnalysisOutcome> RunPythonAsync()
            {
                return await pythonClient.AnalyzeAsync(fullText, cancellationToken);
            }

            async Task<OllamaComparisonOutcome> RunLlmAsync()
            {
                var outcome = await ollamaClient.CompareNewsToCorpusAsync(
                    input.Headline,
                    input.NewsText,
                    corpusForLlm,
                    cancellationToken);

                jobStore.Patch(jobId, s =>
                {
                    s.LlmScore = outcome.Succeeded ? outcome.AlignmentScore : null;
                    s.LlmSummaryPreview = outcome.Succeeded
                        ? TruncateForJob(outcome.Summary)
                        : TruncateForJob(outcome.ErrorMessage);
                    s.Message = outcome.Succeeded
                        ? "Локальная модель (Ollama) завершила сравнение с корпусом."
                        : "Сравнение через Ollama завершилось с ошибкой или отключено; итог будет по эвристике.";
                });

                return outcome;
            }

            var pythonTask = RunPythonAsync();
            var llmTask = RunLlmAsync();
            await Task.WhenAll(pythonTask, llmTask);

            var pythonOutcome = await pythonTask;
            var llmOutcome = await llmTask;

            jobStore.Patch(jobId, s =>
            {
                s.Phase = "HeuristicScoring";
                s.Message = "Подсчёт эвристической оценки (лексика, Python, совпадения с корпусом)…";
            });

            var (lexical, lexicalFragments) = AnalyzeLexicalWithSpans(fullText, lexicons);
            var pythonFragments = pythonOutcome.Fragments;

            var fragments = new List<SuspiciousFragment>();
            fragments.AddRange(lexicalFragments);
            fragments.AddRange(MapPythonFragments(pythonFragments));
            fragments = DeduplicateIdenticalSpans(fragments);
            fragments = CapFragments(fragments);

            var heuristicScore = CalculateScore(weights, lexical, pythonFragments, matches);
            heuristicScore = Math.Clamp(heuristicScore, 0, 100);

            jobStore.Patch(jobId, s =>
            {
                s.HeuristicScore = heuristicScore;
                s.Message = "Эвристический анализ завершён. Формируется итоговая оценка…";
            });

            var (combinedScore, status) = CombineHeuristicAndLlm(heuristicScore, llmOutcome);

            jobStore.Patch(jobId, s =>
            {
                s.Phase = "Combining";
                s.CombinedScore = combinedScore;
                s.Message = "Сохранение результата…";
            });

            var record = new AnalysisRecord
            {
                Headline = input.Headline,
                NewsText = input.NewsText,
                SourceUrl = input.SourceUrl,
                HeuristicReliabilityScore = heuristicScore,
                LlmAlignmentScore = llmOutcome.Succeeded ? llmOutcome.AlignmentScore : null,
                LlmSummary = BuildLlmSummaryLine(llmOutcome),
                ReliabilityScore = combinedScore,
                Status = status,
                Explanation = BuildExplanation(
                    combinedScore,
                    heuristicScore,
                    llmOutcome,
                    fragments.Count,
                    matches.Count,
                    lexical,
                    pythonOutcome),
                SuspiciousFragments = fragments
            };

            db.AnalysisRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);

            jobStore.Patch(jobId, s =>
            {
                s.Phase = "Completed";
                s.RecordId = record.Id;
                s.CombinedScore = combinedScore;
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

    private static string? TruncateForJob(string? text, int max = 280)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private (int combined, VerificationStatus status) CombineHeuristicAndLlm(int heuristicScore, OllamaComparisonOutcome llmOutcome)
    {
        if (!llmOutcome.WasAttempted || !llmOutcome.Succeeded || !llmOutcome.AlignmentScore.HasValue)
        {
            var statusOnlyHeuristic = heuristicScore switch
            {
                >= 70 => VerificationStatus.LikelyReliable,
                <= 40 => VerificationStatus.Suspicious,
                _ => VerificationStatus.NeedsReview
            };
            return (heuristicScore, statusOnlyHeuristic);
        }

        var wh = configuration.GetValue("AnalysisScoring:HeuristicBlendWeight", 0.65);
        var wl = configuration.GetValue("AnalysisScoring:LlmBlendWeight", 0.35);
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
        combined = Math.Clamp(combined, 0, 100);

        var status = combined switch
        {
            >= 70 => VerificationStatus.LikelyReliable,
            <= 40 => VerificationStatus.Suspicious,
            _ => VerificationStatus.NeedsReview
        };

        return (combined, status);
    }

    private static string? BuildLlmSummaryLine(OllamaComparisonOutcome llm)
    {
        if (!llm.WasAttempted)
        {
            return null;
        }

        if (llm.Succeeded)
        {
            var s = string.IsNullOrWhiteSpace(llm.Summary)
                ? $"Модель: alignmentScore={llm.AlignmentScore}."
                : llm.Summary.Trim();
            return s.Length > 3900 ? s[..3900] + "…" : s;
        }

        return string.IsNullOrWhiteSpace(llm.ErrorMessage) ? "Ollama: ошибка без сообщения." : $"Ollama: {llm.ErrorMessage}";
    }

    private async Task<List<OfficialPublication>> LoadPublicationsCorpusAsync(CancellationToken cancellationToken)
    {
        List<OfficialPublication> fromWeb;
        try
        {
            fromWeb = await officialSourceFetcher.FetchAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Official source fetch canceled internally, switching to DB fallback");
            fromWeb = [];
        }

        if (fromWeb.Count == 0)
        {
            logger.LogInformation("No web sources fetched, fallback to database source list");
            fromWeb = await db.OfficialPublications.AsNoTracking().ToListAsync(cancellationToken);
        }

        return fromWeb;
    }

    private static List<OfficialPublication> SelectCorpusForLlm(string newsText, List<OfficialPublication> pubs, int maxSnippets)
    {
        if (pubs.Count == 0)
        {
            return [];
        }

        var ordered = pubs
            .Select(o => (o, Score: CalculateRelevance(newsText, o.Content)))
            .OrderByDescending(x => x.Score)
            .ToList();

        var picked = ordered.Where(x => x.Score > 0).Select(x => x.o).Take(maxSnippets).ToList();
        if (picked.Count > 0)
        {
            return picked;
        }

        return ordered.Select(x => x.o).Take(maxSnippets).ToList();
    }

    private static List<OfficialPublicationMatchVm> RankMatchesToViewModels(string newsText, List<OfficialPublication> pubs) =>
        pubs
            .Select(o => new OfficialPublicationMatchVm
            {
                SourceName = o.SourceName,
                Title = o.Title,
                Url = o.Url,
                RelevanceScore = CalculateRelevance(newsText, o.Content)
            })
            .Where(m => m.RelevanceScore > 15)
            .OrderByDescending(m => m.RelevanceScore)
            .Take(5)
            .ToList();

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

    private async Task<List<OfficialPublicationMatchVm>> GetOfficialMatchesInternalAsync(string text, CancellationToken cancellationToken)
    {
        var pubs = await LoadPublicationsCorpusAsync(cancellationToken);
        return RankMatchesToViewModels(text, pubs);
    }

    private static int CalculateRelevance(string query, string officialContent)
    {
        var tokens = query
            .ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '!', '?', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 3)
            .Distinct()
            .ToHashSet();

        if (tokens.Count == 0)
        {
            return 0;
        }

        var content = officialContent.ToLowerInvariant();
        var overlap = tokens.Count(content.Contains);
        return (int)Math.Round((double)overlap / tokens.Count * 100);
    }

    private static string BuildExplanation(
        int combinedScore,
        int heuristicScore,
        OllamaComparisonOutcome llmOutcome,
        int suspiciousCount,
        int matchCount,
        LexicalFeatures lexical,
        PythonAnalysisOutcome pythonOutcome)
    {
        var lines = new List<string>
        {
            $"Итоговая оценка достоверности (комбинированная): {combinedScore} из 100.",
            $"Эвристическая оценка (лексика, Python, совпадения с корпусом): {heuristicScore} из 100.",
            BuildLlmExplanationLine(llmOutcome),
            $"Выделено фрагментов в тексте (маркеров): {suspiciousCount}.",
            "Счётчики эмоциональной, манипулятивной и оценочной лексики ниже — по правилам приложения (C#); они не зависят от дополнительного модуля Python.",
            $"Релевантных официальных публикаций по ключевым словам: {matchCount}.",
            $"Эмоциональная лексика (словарь, C#): {lexical.EmotionalHits}; манипулятивные фразы (C#): {lexical.ManipulativeHits}; оценочная лексика (C#): {lexical.EvaluativeHits}.",
            $"Верхний регистр (длинные токены): {lexical.UppercaseWords}; восклицательные знаки: {lexical.ExclamationCount}; вопросительные: {lexical.QuestionCount}.",
            $"Даты (цифровые и словесные): {(lexical.HasDates ? "есть" : "нет")}{(lexical.RussianDateHits > 0 ? $", из них словесных формулировок: {lexical.RussianDateHits}" : string.Empty)}.",
            $"Ссылки в тексте: {(lexical.HasLinks ? "есть" : "нет")}; числа: {(lexical.HasNumbers ? "есть" : "нет")}; явные отсылки к источнику: {(lexical.HasSourceCue ? "есть" : "нет")}.",
            BuildPythonExplanationLine(pythonOutcome),
            matchCount > 0
                ? "Сопоставление с официальными материалами: есть пересечения по тексту."
                : "Сопоставление с официальными материалами: релевантных совпадений по выбранным источникам не найдено.",
            "Оценка локальной LLM (Ollama) носит вспомогательный характер и не заменяет экспертизу врача или официальных рекомендаций."
        };

        return string.Join('\n', lines);
    }

    private static string BuildLlmExplanationLine(OllamaComparisonOutcome llm)
    {
        if (!llm.WasAttempted)
        {
            return "Локальная модель (Ollama): отключена в настройках (Ollama:Enabled). Итоговая оценка совпадает с эвристикой.";
        }

        if (llm.Succeeded && llm.AlignmentScore.HasValue)
        {
            var tail = string.IsNullOrWhiteSpace(llm.Summary) ? string.Empty : $" Кратко: {llm.Summary.Trim()}";
            return $"Локальная модель (Ollama): согласованность с выдержками корпуса — {llm.AlignmentScore} из 100.{tail}";
        }

        var err = string.IsNullOrWhiteSpace(llm.ErrorMessage) ? "см. логи приложения" : llm.ErrorMessage.Trim();
        return $"Локальная модель (Ollama): не удалось получить оценку ({err}). Итог рассчитан по эвристике.";
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
            return $"Дополнительный модуль (Python): возвращено {n} фрагментов (словари RU/EN и эвристики в скрипте).";
        }

        return "Дополнительный модуль (Python): выполнен успешно, дополнительных фрагментов не вернул; маркеры в тексте при этом могут полностью относиться к анализу приложения (C#).";
    }

    private static string Normalize(string text) => MultiSpaceRegex().Replace(text.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinksRegex();

    [GeneratedRegex(@"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b|\b\d{4}\b", RegexOptions.IgnoreCase)]
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

    private Lexicons LoadLexicons()
    {
        var basePath = configuration["AnalysisLexicons:BasePath"] ?? "Resources/Lexicons";
        var root = Path.Combine(env.ContentRootPath, basePath);
        return new Lexicons(
            LoadEmotionalLexicons(root),
            ReadLexicon(Path.Combine(root, configuration["AnalysisLexicons:ManipulativeFile"] ?? "manipulative_ru.txt")),
            ReadLexicon(Path.Combine(root, configuration["AnalysisLexicons:EvaluativeFile"] ?? "evaluative_ru.txt")),
            ReadLexicon(Path.Combine(root, configuration["AnalysisLexicons:SourceCuesFile"] ?? "source_cues_ru.txt")));
    }

    /// <summary>
    /// Основной эмоциональный словарь + опциональный emotional_ru_rusentilex.txt (см. python/tools/build_lexicons.py).
    /// </summary>
    private HashSet<string> LoadEmotionalLexicons(string root)
    {
        var mainName = configuration["AnalysisLexicons:EmotionalFile"] ?? "emotional_ru.txt";
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
        public int BaseScore { get; set; } = 55;
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
