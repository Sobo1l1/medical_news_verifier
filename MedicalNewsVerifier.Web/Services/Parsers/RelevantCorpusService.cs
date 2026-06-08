using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public interface IRelevantCorpusService
{
    Task<List<OfficialPublication>> FetchRelevantAsync(
        string headline,
        string newsText,
        CancellationToken cancellationToken);

    Task<List<OfficialPublication>> PersistUsedAsync(
        IEnumerable<OfficialPublication> publications,
        CancellationToken cancellationToken);
}

public sealed class RelevantCorpusService(
    AppDbContext db,
    SourceParserRegistry parserRegistry,
    ReferencedOfficialUrlFetcher referencedUrlFetcher,
    OfficialStatisticsEnricher statisticsEnricher,
    IConfiguration configuration,
    ILogger<RelevantCorpusService> logger) : IRelevantCorpusService
{
    public async Task<List<OfficialPublication>> FetchRelevantAsync(
        string headline,
        string newsText,
        CancellationToken cancellationToken)
    {
        var maxArticles = configuration.GetValue("SourceParsers:MaxArticlesPerAnalysis", 5);
        var minRelevance = configuration.GetValue("SourceParsers:MinRelevanceScore", 15);
        var newsTopic = RelevanceScoring.DetectDominantTopic(headline, newsText);
        var expectsStats = RelevanceScoring.QueryExpectsStatistics(headline, newsText);

        var query = new SourceSearchQuery
        {
            Headline = headline,
            NewsText = newsText,
            MaxResults = maxArticles,
            MinRelevanceScore = expectsStats ? Math.Max(minRelevance, 18) : minRelevance
        };

        var corpusInDb = await db.OfficialPublications
            .Include(p => p.TrustedSource)
            .AsNoTracking()
            .Where(p => p.TrustedSource!.IsEnabled)
            .ToListAsync(cancellationToken);

        var corpusByUrl = corpusInDb
            .GroupBy(p => HtmlTextExtractor.NormalizeUrl(p.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var candidates = new List<(ParsedPublication Parsed, TrustedSource Source, int Score)>();
        var candidateUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var minzdravSource = await db.TrustedSources
            .AsNoTracking()
            .Where(s => s.IsEnabled && EF.Functions.ILike(s.BaseUrl, "%minzdrav%"))
            .FirstOrDefaultAsync(cancellationToken);
        var statsSource = minzdravSource ?? new TrustedSource
        {
            Name = "ЕМИСС / официальная статистика",
            BaseUrl = "https://fedstat.ru/",
            IsEnabled = true
        };

        // 1. Парсинг источников (с проверкой дубликатов в корпусе)
        try
        {
            var fromStats = await statisticsEnricher.EnrichAsync(headline, newsText, query, cancellationToken);
            foreach (var pub in fromStats)
            {
                var score = RelevanceScoring.CalculateMatchScore(headline, newsText, $"{pub.Title} {pub.Content}");
                TryAddCandidate(candidates, candidateUrls, corpusByUrl, corpusInDb, pub, statsSource, score + 40);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Statistics enricher failed");
        }

        try
        {
            var fromReferencedUrls = await referencedUrlFetcher.FetchFromNewsTextAsync(
                headline, newsText, query, cancellationToken);
            var urlSource = minzdravSource ?? statsSource;

            foreach (var pub in fromReferencedUrls)
            {
                var score = RelevanceScoring.CalculateMatchScore(headline, newsText, $"{pub.Title} {pub.Content}");
                TryAddCandidate(candidates, candidateUrls, corpusByUrl, corpusInDb, pub, urlSource, score + 25);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Referenced official URL fetch failed");
        }

        var sources = await db.TrustedSources
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        foreach (var source in sources)
        {
            if (newsTopic == NewsTopic.Oncology
                && source.BaseUrl.Contains("rospotrebnadzor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parser = parserRegistry.FindParser(source);
            if (parser is null)
            {
                continue;
            }

            var perSourceQuery = new SourceSearchQuery
            {
                Headline = query.Headline,
                NewsText = query.NewsText,
                MaxResults = maxArticles,
                MinRelevanceScore = query.MinRelevanceScore
            };

            try
            {
                var parsed = await parser.SearchRelevantAsync(source, perSourceQuery, cancellationToken);
                foreach (var pub in parsed)
                {
                    var combined = $"{pub.Title} {pub.Content}";
                    if (newsTopic == NewsTopic.Oncology && RelevanceScoring.IsPrimarilyRespiratory(combined))
                    {
                        continue;
                    }

                    var score = RelevanceScoring.CalculateMatchScore(headline, newsText, combined);
                    if (expectsStats && !RelevanceScoring.CandidateHasStatistics(combined) && score < 25)
                    {
                        continue;
                    }

                    TryAddCandidate(candidates, candidateUrls, corpusByUrl, corpusInDb, pub, source, score);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Parser {ParserKey} failed for {SourceName}", parser.SourceKey, source.Name);
            }
        }

        // 2. Корпус БД — только материалы, ещё не добавленные при парсинге
        foreach (var pub in corpusInDb)
        {
            var normalizedUrl = HtmlTextExtractor.NormalizeUrl(pub.Url);
            if (candidateUrls.Contains(normalizedUrl))
            {
                continue;
            }

            var combined = $"{pub.Title} {pub.Content}";
            if (newsTopic == NewsTopic.Oncology && RelevanceScoring.IsPrimarilyRespiratory(combined))
            {
                continue;
            }

            var score = RelevanceScoring.CalculateMatchScore(headline, newsText, combined);
            if (score >= query.MinRelevanceScore)
            {
                TryAddCandidate(
                    candidates,
                    candidateUrls,
                    corpusByUrl,
                    corpusInDb,
                    ToParsed(pub),
                    pub.TrustedSource!,
                    score);
            }
        }

        var selected = SelectTopCandidates(candidates, maxArticles);

        var maxScore = selected.Count > 0 ? selected.Max(c => c.Score) : 0;
        var staleCorpus = selected.Count > 0
            && selected.All(c => c.Parsed.PublishedAtUtc < DateTime.UtcNow.AddHours(-24));

        if (selected.Count < maxArticles || maxScore < 28 || staleCorpus)
        {
            logger.LogInformation(
                "Expanding corpus search: count={Count}, maxScore={MaxScore}, staleCorpus={Stale}",
                selected.Count,
                maxScore,
                staleCorpus);

            var expandedQuery = new SourceSearchQuery
            {
                Headline = query.Headline,
                NewsText = query.NewsText,
                MaxResults = maxArticles + 5,
                MinRelevanceScore = Math.Max(8, query.MinRelevanceScore - 7)
            };

            foreach (var source in sources)
            {
                if (newsTopic == NewsTopic.Oncology
                    && source.BaseUrl.Contains("rospotrebnadzor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parser = parserRegistry.FindParser(source);
                if (parser is null)
                {
                    continue;
                }

                try
                {
                    var parsed = await parser.SearchRelevantAsync(source, expandedQuery, cancellationToken);
                    foreach (var pub in parsed)
                    {
                        var combined = $"{pub.Title} {pub.Content}";
                        if (newsTopic == NewsTopic.Oncology && RelevanceScoring.IsPrimarilyRespiratory(combined))
                        {
                            continue;
                        }

                        var score = RelevanceScoring.CalculateMatchScore(headline, newsText, combined);
                        TryAddCandidate(candidates, candidateUrls, corpusByUrl, corpusInDb, pub, source, score);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Expanded parser {ParserKey} failed for {SourceName}", parser.SourceKey, source.Name);
                }
            }

            selected = SelectTopCandidates(candidates, maxArticles + 5);
        }

        logger.LogInformation(
            "Relevant corpus: {Count} publication(s) selected for analysis (from {CandidateCount} candidates)",
            selected.Count,
            candidates.Count);

        return selected.Take(maxArticles).Select(c => new OfficialPublication
        {
            TrustedSource = c.Source,
            TrustedSourceId = c.Source.Id,
            Title = c.Parsed.Title,
            Content = c.Parsed.Content,
            Url = HtmlTextExtractor.NormalizeUrl(c.Parsed.Url),
            PublishedAtUtc = c.Parsed.PublishedAtUtc
        }).ToList();
    }

    public async Task<List<OfficialPublication>> PersistUsedAsync(
        IEnumerable<OfficialPublication> publications,
        CancellationToken cancellationToken)
    {
        var result = new List<OfficialPublication>();
        foreach (var pub in publications)
        {
            if (string.IsNullOrWhiteSpace(pub.Url) || pub.Content.Length < 120)
            {
                continue;
            }

            var normalizedUrl = HtmlTextExtractor.NormalizeUrl(pub.Url);
            var existing = await db.OfficialPublications
                .Include(p => p.TrustedSource)
                .FirstOrDefaultAsync(p => p.Url == normalizedUrl, cancellationToken);

            if (existing is not null)
            {
                existing.Title = pub.Title;
                existing.Content = pub.Content;
                existing.PublishedAtUtc = pub.PublishedAtUtc;
                if (pub.TrustedSourceId > 0)
                {
                    existing.TrustedSourceId = pub.TrustedSourceId;
                }

                result.Add(existing);
            }
            else
            {
                var trustedSourceId = pub.TrustedSourceId;
                if (trustedSourceId <= 0 && pub.TrustedSource is not null)
                {
                    trustedSourceId = pub.TrustedSource.Id;
                }

                if (trustedSourceId <= 0)
                {
                    continue;
                }

                var entity = new OfficialPublication
                {
                    TrustedSourceId = trustedSourceId,
                    Title = pub.Title,
                    Content = pub.Content,
                    Url = normalizedUrl,
                    PublishedAtUtc = pub.PublishedAtUtc
                };
                db.OfficialPublications.Add(entity);
                result.Add(entity);
            }
        }

        if (result.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            foreach (var pub in result)
            {
                await db.Entry(pub).Reference(p => p.TrustedSource).LoadAsync(cancellationToken);
            }

            logger.LogInformation("Persisted {Count} publication(s) used in verification to corpus", result.Count);
        }

        return result;
    }

    private static List<(ParsedPublication Parsed, TrustedSource Source, int Score)> SelectTopCandidates(
        List<(ParsedPublication Parsed, TrustedSource Source, int Score)> candidates,
        int take) =>
        candidates
            .GroupBy(c => HtmlTextExtractor.NormalizeUrl(c.Parsed.Url), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => RelevanceScoring.CandidateHasStatistics($"{c.Parsed.Title} {c.Parsed.Content}") ? 1 : 0)
            .Take(take)
            .ToList();

    private static void TryAddCandidate(
        List<(ParsedPublication Parsed, TrustedSource Source, int Score)> candidates,
        HashSet<string> candidateUrls,
        Dictionary<string, OfficialPublication> corpusByUrl,
        List<OfficialPublication> corpusInDb,
        ParsedPublication parsed,
        TrustedSource source,
        int score)
    {
        var normalizedUrl = HtmlTextExtractor.NormalizeUrl(parsed.Url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return;
        }

        if (corpusByUrl.TryGetValue(normalizedUrl, out var existingByUrl))
        {
            parsed = ToParsed(existingByUrl);
            source = existingByUrl.TrustedSource ?? source;
            normalizedUrl = HtmlTextExtractor.NormalizeUrl(parsed.Url);
        }
        else
        {
            var similar = FindSimilarPublication(corpusInDb, parsed);
            if (similar is not null)
            {
                parsed = ToParsed(similar);
                source = similar.TrustedSource ?? source;
                normalizedUrl = HtmlTextExtractor.NormalizeUrl(parsed.Url);
            }
        }

        if (candidateUrls.Contains(normalizedUrl))
        {
            return;
        }

        candidates.Add((parsed, source, score));
        candidateUrls.Add(normalizedUrl);
    }

    private static OfficialPublication? FindSimilarPublication(List<OfficialPublication> corpus, ParsedPublication parsed)
    {
        var titleNorm = NormalizeTitle(parsed.Title);
        if (titleNorm.Length < 10)
        {
            return null;
        }

        foreach (var pub in corpus)
        {
            var existingNorm = NormalizeTitle(pub.Title);
            if (TitlesAreSimilar(titleNorm, existingNorm))
            {
                return pub;
            }
        }

        return null;
    }

    private static bool TitlesAreSimilar(string a, string b)
    {
        if (a == b)
        {
            return true;
        }

        if (a.Length < 10 || b.Length < 10)
        {
            return false;
        }

        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length > b.Length ? a : b;
        return longer.Contains(shorter, StringComparison.Ordinal);
    }

    private static string NormalizeTitle(string title) =>
        string.Join(' ', title.ToLowerInvariant()
            .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static ParsedPublication ToParsed(OfficialPublication pub) => new()
    {
        Title = pub.Title,
        Content = pub.Content,
        Url = pub.Url,
        PublishedAtUtc = pub.PublishedAtUtc
    };
}
