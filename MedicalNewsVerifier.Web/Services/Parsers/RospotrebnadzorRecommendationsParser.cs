using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public sealed class RospotrebnadzorRecommendationsParser(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<RospotrebnadzorRecommendationsParser> logger) : ISourceParser
{
    private static readonly string[] TopicKeywords =
    [
        "грипп", "орви", "простуд", "маск", "вакцин", "антисепт", "респиратор",
        "температур", "каш", "насморк", "инфекц", "вирус", "эпидем"
    ];

    public string SourceKey => "rospotrebnadzor";

    public bool CanParse(TrustedSource source) =>
        source.BaseUrl.Contains("rospotrebnadzor", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ParsedPublication>> SearchRelevantAsync(
        TrustedSource source,
        SourceSearchQuery query,
        CancellationToken cancellationToken)
    {
        var topic = RelevanceScoring.DetectDominantTopic(query.Headline, query.NewsText);
        if (topic == NewsTopic.Oncology)
        {
            logger.LogDebug("Rospotrebnadzor parser: skipped — news topic is oncology, not flu/ORVI");
            return [];
        }

        var searchText = $"{query.Headline} {query.NewsText}";
        if (topic != NewsTopic.Respiratory
            && !RelevanceScoring.ContainsAnyKeyword(searchText, TopicKeywords)
            && RelevanceScoring.CalculateMatchScore(query.Headline, query.NewsText, "грипп орви рекомендации") < query.MinRelevanceScore)
        {
            logger.LogDebug("Rospotrebnadzor parser: news text not related to flu/ORVI recommendations, skipping");
            return [];
        }

        var pageUrl = configuration["SourceParsers:Rospotrebnadzor:RecommendationsUrl"]
            ?? source.BaseUrl;

        if (string.IsNullOrWhiteSpace(pageUrl))
        {
            return [];
        }

        try
        {
            var html = await httpClient.GetStringAsync(pageUrl, cancellationToken);
            var title = HtmlTextExtractor.ExtractTitle(html);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Рекомендации населению в период подъема заболеваемости гриппом и ОРВИ";
            }

            var content = await HtmlTextExtractor.ExtractArticleTextAsync(
                html,
                cancellationToken,
                ".page-content",
                ".content",
                "article",
                "main");

            if (content.Length < 120)
            {
                content = HtmlTextExtractor.StripHtmlToText(html);
            }

            if (content.Length < 120)
            {
                logger.LogWarning("Rospotrebnadzor parser: insufficient content at {Url}", pageUrl);
                return [];
            }

        var relevance = RelevanceScoring.CalculateMatchScore(query.Headline, query.NewsText, $"{title} {content}");
        if (relevance < query.MinRelevanceScore
            && !RelevanceScoring.MatchesTopicCluster(query.Headline, query.NewsText, $"{title} {content}"))
            {
                logger.LogDebug(
                    "Rospotrebnadzor parser: relevance {Score} below threshold {Min}",
                    relevance,
                    query.MinRelevanceScore);
                return [];
            }

            logger.LogInformation("Rospotrebnadzor parser: 1 relevant publication (score {Score})", relevance);

            return
            [
                new ParsedPublication
                {
                    Title = title.Length <= 500 ? title : title[..497] + "...",
                    Content = HtmlTextExtractor.TruncateContent(content),
                    Url = HtmlTextExtractor.NormalizeUrl(pageUrl),
                    PublishedAtUtc = DateTime.UtcNow
                }
            ];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rospotrebnadzor parser failed for source {SourceName}", source.Name);
            throw;
        }
    }
}
