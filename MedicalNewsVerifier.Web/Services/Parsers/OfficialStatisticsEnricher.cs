using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public sealed class OfficialStatisticsEnricher(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<OfficialStatisticsEnricher> logger)
{
    public async Task<IReadOnlyList<ParsedPublication>> EnrichAsync(
        string headline,
        string newsText,
        SourceSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!RelevanceScoring.QueryExpectsStatistics(headline, newsText))
        {
            return [];
        }

        var urls = configuration.GetSection("SourceParsers:StatisticsUrls").Get<string[]>()
            ?? DefaultStatisticsUrls;

        var topic = RelevanceScoring.DetectDominantTopic(headline, newsText);
        if (topic != NewsTopic.Oncology && topic != NewsTopic.General)
        {
            return [];
        }

        var results = new List<ParsedPublication>();
        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(query.MaxResults))
        {
            try
            {
                var html = await httpClient.GetStringAsync(url, cancellationToken);
                var title = HtmlTextExtractor.ExtractTitle(html);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = "Официальная статистика";
                }

                var content = await HtmlTextExtractor.ExtractArticleTextAsync(
                    html,
                    cancellationToken,
                    "main",
                    "article",
                    ".content",
                    "table",
                    "#content");

                if (content.Length < 80)
                {
                    content = HtmlTextExtractor.StripHtmlToText(html);
                }

                if (content.Length < 80)
                {
                    continue;
                }

                var score = RelevanceScoring.CalculateMatchScore(headline, newsText, $"{title} {content}");
                if (score < query.MinRelevanceScore && !RelevanceScoring.CandidateHasStatistics($"{title} {content}"))
                {
                    continue;
                }

                results.Add(new ParsedPublication
                {
                    Title = title.Length <= 500 ? title : title[..497] + "...",
                    Content = HtmlTextExtractor.TruncateContent(content),
                    Url = HtmlTextExtractor.NormalizeUrl(url),
                    PublishedAtUtc = DateTime.UtcNow
                });

                logger.LogInformation("Statistics enricher fetched {Url} (score {Score})", url, score);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Statistics enricher failed for {Url}", url);
            }
        }

        return results;
    }

    private static readonly string[] DefaultStatisticsUrls =
    [
        "https://fedstat.ru/indicator/41715",
        "https://www.rosminzdrav.ru/ministry/61/22/stranitsa-9799/statistika-i-registr/natsionalnyy-rakologicheskiy-registr"
    ];
}
