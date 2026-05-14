using System.Net;
using System.Text.RegularExpressions;
using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

public partial class OfficialSourceFetcher(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<OfficialSourceFetcher> logger) : IOfficialSourceFetcher
{
    public async Task<List<OfficialPublication>> FetchAsync(CancellationToken cancellationToken)
    {
        var urls = configuration.GetSection("OfficialSources:Urls").Get<string[]>() ?? [];
        var timeoutSeconds = configuration.GetValue<int?>("OfficialSources:TimeoutSeconds") ?? 8;

        var result = new List<OfficialPublication>();
        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var response = await httpClient.GetAsync(url, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Source {Url} returned status {Status}", url, response.StatusCode);
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var content = CompactText(StripHtmlRegex().Replace(html, " "));
                if (content.Length < 120)
                {
                    continue;
                }

                result.Add(new OfficialPublication
                {
                    SourceName = GetSourceName(url),
                    Title = ExtractTitle(html),
                    Url = url,
                    Content = content[..Math.Min(content.Length, 5000)],
                    PublishedAtUtc = DateTime.UtcNow
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Timeout while fetching official source {Url}", url);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch official source {Url}", url);
            }
        }

        return result;
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);
        if (!match.Success)
        {
            return "Официальная публикация";
        }

        return WebUtility.HtmlDecode(CompactText(match.Groups[1].Value));
    }

    private static string GetSourceName(string url)
    {
        if (url.Contains("minzdrav", StringComparison.OrdinalIgnoreCase)) return "Минздрав РФ";
        if (url.Contains("grls", StringComparison.OrdinalIgnoreCase)) return "ГРЛС";
        if (url.Contains("rospotrebnadzor", StringComparison.OrdinalIgnoreCase)) return "Роспотребнадзор";
        return new Uri(url).Host;
    }

    private static string CompactText(string text) => MultiSpaceRegex().Replace(text, " ").Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripHtmlRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
