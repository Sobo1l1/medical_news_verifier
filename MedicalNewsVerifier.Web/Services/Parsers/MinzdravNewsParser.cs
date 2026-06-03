using System.Globalization;
using System.Xml.Linq;
using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public sealed class MinzdravNewsParser(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<MinzdravNewsParser> logger) : ISourceParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public string SourceKey => "minzdrav";

    public bool CanParse(TrustedSource source) =>
        source.BaseUrl.Contains("minzdrav.gov.ru", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ParsedPublication>> SearchRelevantAsync(
        TrustedSource source,
        SourceSearchQuery query,
        CancellationToken cancellationToken)
    {
        var feedUrl = configuration["SourceParsers:Minzdrav:FeedUrl"] ?? "https://minzdrav.gov.ru/news.atom";
        var maxFeedScan = configuration.GetValue("SourceParsers:Minzdrav:MaxFeedScan", 400);

        try
        {
            var feedXml = await httpClient.GetStringAsync(feedUrl, cancellationToken);
            var feed = XDocument.Parse(feedXml);
            var entries = feed.Root?
                .Elements(Atom + "entry")
                .Take(maxFeedScan)
                .ToList() ?? [];

            var scored = ScoreFeedEntries(entries, query);
            var topic = RelevanceScoring.DetectDominantTopic(query.Headline, query.NewsText);

            var selected = scored
                .Where(x => (topic != NewsTopic.Oncology || !RelevanceScoring.IsPrimarilyRespiratory(x.Preview))
                    && (x.Score >= query.MinRelevanceScore
                        || RelevanceScoring.MatchesTopicCluster(query.Headline, query.NewsText, x.Preview)))
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => RelevanceScoring.CandidateHasStatistics(x.Preview) ? 1 : 0)
                .Take(query.MaxResults)
                .ToList();

            if (selected.Count == 0 && RelevanceScoring.QueryExpectsStatistics(query.Headline, query.NewsText))
            {
                selected = scored
                    .Where(x => !RelevanceScoring.IsPrimarilyRespiratory(x.Preview)
                        && RelevanceScoring.CountKeywordHitsForOncology(x.Preview) >= 1)
                    .OrderByDescending(x => x.Score)
                    .Take(Math.Min(2, query.MaxResults))
                    .ToList();
            }
            else if (selected.Count == 0)
            {
                selected = scored
                    .Where(x => x.Score > 0
                        && !RelevanceScoring.IsPrimarilyRespiratory(x.Preview))
                    .OrderByDescending(x => x.Score)
                    .Take(Math.Min(3, query.MaxResults))
                    .ToList();
            }

            var results = new List<ParsedPublication>();
            foreach (var item in selected)
            {
                var content = item.Pub.Content;
                var fullText = await TryFetchFullArticleAsync(item.Pub.Url, cancellationToken);
                if (fullText.Length >= 120)
                {
                    content = HtmlTextExtractor.TruncateContent(fullText);
                }

                var finalScore = RelevanceScoring.CalculateMatchScore(
                    query.Headline,
                    query.NewsText,
                    $"{item.Pub.Title} {content}");

                if (content.Length >= 120 && finalScore >= query.MinRelevanceScore)
                {
                    results.Add(new ParsedPublication
                    {
                        Title = item.Pub.Title,
                        Url = item.Pub.Url,
                        Content = content,
                        PublishedAtUtc = item.Pub.PublishedAtUtc
                    });
                }
            }

            logger.LogInformation(
                "Minzdrav parser: {Count} relevant publication(s) (scanned {Scanned} feed entries)",
                results.Count,
                entries.Count);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Minzdrav parser failed for source {SourceName}", source.Name);
            throw;
        }
    }

    private static List<(ParsedPublication Pub, int Score, string Preview)> ScoreFeedEntries(
        List<XElement> entries,
        SourceSearchQuery query)
    {
        var scored = new List<(ParsedPublication Pub, int Score, string Preview)>();
        var topic = RelevanceScoring.DetectDominantTopic(query.Headline, query.NewsText);
        foreach (var entry in entries)
        {
            var title = (entry.Element(Atom + "title")?.Value ?? string.Empty).Trim();
            var link = entry.Elements(Atom + "link")
                .FirstOrDefault(e => (string?)e.Attribute("rel") is null or "alternate")
                ?.Attribute("href")?.Value
                ?? entry.Element(Atom + "id")?.Value
                ?? string.Empty;
            var summary = entry.Element(Atom + "content")?.Value
                ?? entry.Element(Atom + "summary")?.Value
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            var preview = $"{title} {summary}";
            if (topic == NewsTopic.Oncology && RelevanceScoring.IsPrimarilyRespiratory(preview))
            {
                continue;
            }

            var score = RelevanceScoring.CalculateMatchScore(query.Headline, query.NewsText, preview);
            var publishedRaw = entry.Element(Atom + "published")?.Value
                ?? entry.Element(Atom + "updated")?.Value;

            scored.Add((new ParsedPublication
            {
                Title = TruncateTitle(title),
                Content = HtmlTextExtractor.TruncateContent(CompactText(summary)),
                Url = HtmlTextExtractor.NormalizeUrl(link),
                PublishedAtUtc = ParseAtomDate(publishedRaw)
            }, score, preview));
        }

        return scored;
    }

    private async Task<string> TryFetchFullArticleAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var html = await httpClient.GetStringAsync(url, cancellationToken);
            return await HtmlTextExtractor.ExtractArticleTextAsync(
                html,
                cancellationToken,
                ".page-content",
                ".news-detail",
                ".article",
                ".content-page",
                "article");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch full Minzdrav article {Url}", url);
            return string.Empty;
        }
    }

    private static DateTime ParseAtomDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UtcNow;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        return DateTime.UtcNow;
    }

    private static string TruncateTitle(string title) =>
        title.Length <= 500 ? title : title[..497] + "...";

    private static string CompactText(string text) =>
        string.Join(' ', text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
