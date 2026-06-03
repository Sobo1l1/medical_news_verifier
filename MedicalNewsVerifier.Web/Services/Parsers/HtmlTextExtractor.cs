using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public static partial class HtmlTextExtractor
{
    public const int MaxContentLength = 5000;

    public static async Task<string> ExtractArticleTextAsync(
        string html,
        CancellationToken cancellationToken = default,
        params string[] contentSelectors)
    {
        var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

        foreach (var selector in contentSelectors)
        {
            var node = document.QuerySelector(selector);
            var text = ExtractTextFromNode(node);
            if (text.Length >= 120)
            {
                return TruncateContent(text);
            }
        }

        var main = document.QuerySelector("main")
            ?? document.QuerySelector("article")
            ?? document.QuerySelector(".content")
            ?? document.QuerySelector("#content")
            ?? document.Body;

        return TruncateContent(ExtractTextFromNode(main));
    }

    public static string ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);
        if (!match.Success)
        {
            return string.Empty;
        }

        return CompactText(WebUtility.HtmlDecode(match.Groups[1].Value));
    }

    public static string StripHtmlToText(string html) =>
        TruncateContent(CompactText(StripHtmlRegex().Replace(html, " ")));

    public static string TruncateContent(string text)
    {
        var compact = CompactText(text);
        return compact.Length <= MaxContentLength
            ? compact
            : compact[..MaxContentLength];
    }

    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.TrimEnd('/');
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty, Query = string.Empty };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string ExtractTextFromNode(IElement? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var clone = (IElement)node.Clone(true);
        foreach (var el in clone.QuerySelectorAll("script, style, nav, header, footer, noscript, .cookie, .modal"))
        {
            el.Remove();
        }

        return CompactText(clone.TextContent);
    }

    private static string CompactText(string text) => MultiSpaceRegex().Replace(text, " ").Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripHtmlRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
