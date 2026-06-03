using System.Text.RegularExpressions;
using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public partial class ReferencedOfficialUrlFetcher(
    HttpClient httpClient,
    ILogger<ReferencedOfficialUrlFetcher> logger)
{
    private static readonly (string DomainFragment, string SourceLabel)[] OfficialDomains =
    [
        ("minzdrav.gov.ru", "Минздрав РФ"),
        ("rosminzdrav.ru", "Минздрав РФ"),
        ("fedstat.ru", "ЕМИСС / Росстат"),
        ("rosstat.gov.ru", "Росстат"),
        ("glavonco.ru", "НМИЦ онкологии"),
        ("rospotrebnadzor.ru", "Роспотребнадзор")
    ];

    [GeneratedRegex(@"https?://[^\s<>""')\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public async Task<IReadOnlyList<ParsedPublication>> FetchFromNewsTextAsync(
        string headline,
        string newsText,
        SourceSearchQuery query,
        CancellationToken cancellationToken)
    {
        var combined = $"{headline}\n{newsText}";
        var urls = UrlRegex()
            .Matches(combined)
            .Select(m => m.Value.TrimEnd('.', ',', ';'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(IsOfficialUrl)
            .Take(query.MaxResults)
            .ToList();

        if (urls.Count == 0)
        {
            return [];
        }

        var results = new List<ParsedPublication>();
        foreach (var url in urls)
        {
            try
            {
                var pub = await FetchUrlAsync(url, cancellationToken);
                if (pub is null)
                {
                    continue;
                }

                var score = RelevanceScoring.CalculateMatchScore(headline, newsText, $"{pub.Title} {pub.Content}");
                if (RelevanceScoring.DetectDominantTopic(headline, newsText) == NewsTopic.Oncology
                    && url.Contains("rospotrebnadzor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (score >= query.MinRelevanceScore
                    || RelevanceScoring.MatchesTopicCluster(headline, newsText, $"{pub.Title} {pub.Content}"))
                {
                    results.Add(pub);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to fetch referenced official URL {Url}", url);
            }
        }

        logger.LogInformation(
            "Referenced URL fetcher: {Count} publication(s) from {UrlCount} official link(s) in news text",
            results.Count,
            urls.Count);

        return results;
    }

    private static bool IsOfficialUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return OfficialDomains.Any(d => host.Contains(d.DomainFragment, StringComparison.Ordinal));
    }

    private async Task<ParsedPublication?> FetchUrlAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var title = HtmlTextExtractor.ExtractTitle(html);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Официальный источник";
        }

        var content = await HtmlTextExtractor.ExtractArticleTextAsync(
            html,
            cancellationToken,
            "main",
            "article",
            ".content",
            ".page-content",
            "#content",
            "table");

        if (content.Length < 80)
        {
            content = HtmlTextExtractor.StripHtmlToText(html);
        }

        if (content.Length < 80)
        {
            return null;
        }

        return new ParsedPublication
        {
            Title = title.Length <= 500 ? title : title[..497] + "...",
            Content = HtmlTextExtractor.TruncateContent(content),
            Url = HtmlTextExtractor.NormalizeUrl(url),
            PublishedAtUtc = DateTime.UtcNow
        };
    }
}
