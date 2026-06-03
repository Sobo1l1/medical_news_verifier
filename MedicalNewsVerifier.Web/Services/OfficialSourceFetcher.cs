using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services.Parsers;

namespace MedicalNewsVerifier.Web.Services;

public partial class OfficialSourceFetcher(
    HttpClient httpClient,
    IConfiguration configuration,
    SourceParserRegistry parserRegistry,
    ILogger<OfficialSourceFetcher> logger) : IOfficialSourceFetcher
{
    public async Task<List<OfficialPublication>> FetchRelevantAsync(
        string headline,
        string newsText,
        CancellationToken cancellationToken)
    {
        var urls = configuration.GetSection("OfficialSources:Urls").Get<string[]>() ?? [];
        var timeoutSeconds = configuration.GetValue<int?>("OfficialSources:TimeoutSeconds") ?? 8;
        var maxArticles = configuration.GetValue("SourceParsers:MaxArticlesPerAnalysis", 5);
        var minRelevance = configuration.GetValue("SourceParsers:MinRelevanceScore", 10);

        var query = new SourceSearchQuery
        {
            Headline = headline,
            NewsText = newsText,
            MaxResults = maxArticles,
            MinRelevanceScore = minRelevance
        };

        var searchText = $"{headline} {newsText}";
        var candidates = new List<(ParsedPublication Parsed, string SourceName, int Score)>();

        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            var source = new TrustedSource
            {
                Name = GetSourceName(url!),
                BaseUrl = url!,
                IsEnabled = true
            };

            var parser = parserRegistry.FindParser(source);
            if (parser is not null)
            {
                try
                {
                    var parsed = await parser.SearchRelevantAsync(source, query, cancellationToken);
                    foreach (var pub in parsed)
                    {
                        var score = RelevanceScoring.CalculateRelevance(headline, newsText, $"{pub.Title} {pub.Content}");
                        candidates.Add((pub, source.Name, score));
                    }

                    continue;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Parser {ParserKey} failed for {Url}", parser.SourceKey, url);
                }
            }

            var generic = await FetchGenericPublicationAsync(url!, timeoutSeconds, cancellationToken);
            if (generic is not null)
            {
                var score = RelevanceScoring.CalculateRelevance(headline, newsText, generic.Content);
                if (score >= minRelevance)
                {
                    candidates.Add((
                        new ParsedPublication
                        {
                            Title = generic.Title,
                            Content = generic.Content,
                            Url = generic.Url,
                            PublishedAtUtc = generic.PublishedAtUtc
                        },
                        source.Name,
                        score));
                }
            }
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .Take(maxArticles)
            .Select(c => new OfficialPublication
            {
                TrustedSource = new TrustedSource { Name = c.SourceName, BaseUrl = c.Parsed.Url, IsEnabled = true },
                Title = c.Parsed.Title,
                Url = c.Parsed.Url,
                Content = c.Parsed.Content,
                PublishedAtUtc = c.Parsed.PublishedAtUtc
            })
            .ToList();
    }

    private async Task<OfficialPublication?> FetchGenericPublicationAsync(
        string url,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var response = await httpClient.GetAsync(url, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Source {Url} returned status {Status}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var content = await HtmlTextExtractor.ExtractArticleTextAsync(html, timeoutCts.Token);
            if (content.Length < 120)
            {
                content = HtmlTextExtractor.StripHtmlToText(html);
            }

            if (content.Length < 120)
            {
                return null;
            }

            return new OfficialPublication
            {
                TrustedSource = new TrustedSource { Name = GetSourceName(url), BaseUrl = url, IsEnabled = true },
                Title = ExtractTitle(html),
                Url = url,
                Content = content,
                PublishedAtUtc = DateTime.UtcNow
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timeout while fetching official source {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch official source {Url}", url);
            return null;
        }
    }

    private static string ExtractTitle(string html)
    {
        var title = HtmlTextExtractor.ExtractTitle(html);
        return string.IsNullOrWhiteSpace(title) ? "Официальная публикация" : title;
    }

    private static string GetSourceName(string url)
    {
        if (url.Contains("minzdrav", StringComparison.OrdinalIgnoreCase)) return "Минздрав РФ";
        if (url.Contains("grls", StringComparison.OrdinalIgnoreCase)) return "ГРЛС";
        if (url.Contains("rospotrebnadzor", StringComparison.OrdinalIgnoreCase)) return "Роспотребнадзор";
        return new Uri(url).Host;
    }
}
