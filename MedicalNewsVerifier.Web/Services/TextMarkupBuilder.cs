using System.Net;
using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

/// <summary>
/// Собирает безопасный HTML с подсветкой фрагментов по смещениям в исходной строке.
/// </summary>
public static class TextMarkupBuilder
{
    private const int MaxFragmentsForHtml = 400;

    /// <summary>
    /// Удаляет пересекающиеся интервалы: оставляет более ранний по Start; при пересечении отбрасывает следующий.
    /// </summary>
    public static List<SuspiciousFragment> DedupeNonOverlapping(IEnumerable<SuspiciousFragment> fragments)
    {
        var withSpan = fragments
            .Where(f => f.StartOffset >= 0 && f.EndOffset > f.StartOffset)
            .OrderBy(f => f.StartOffset)
            .ThenByDescending(f => f.EndOffset - f.StartOffset)
            .ToList();

        var result = new List<SuspiciousFragment>();
        var lastEnd = -1;
        foreach (var f in withSpan)
        {
            if (f.StartOffset < lastEnd)
            {
                continue;
            }

            result.Add(f);
            lastEnd = f.EndOffset;
        }

        return result;
    }

    /// <summary>
    /// Заголовок (жирным) и текст новости с разрывом; смещения фрагментов остаются в <see cref="AnalyzedDocument.FullText"/>.
    /// </summary>
    public static string BuildStructuredDocumentHtml(AnalyzedDocument doc, IReadOnlyList<SuspiciousFragment> fragments)
    {
        var full = doc.FullText;
        if (string.IsNullOrEmpty(full))
        {
            return string.Empty;
        }

        var bodyStart = Math.Clamp(doc.BodyStart, 0, full.Length);
        var headHtml = BuildHighlightedHtmlForRange(full, fragments, 0, bodyStart);
        var bodyHtml = BuildHighlightedHtmlForRange(full, fragments, bodyStart, full.Length);
        return $"""
<div class="markup-headline"><strong>{headHtml}</strong></div><div class="markup-body-gap" aria-hidden="true"></div><div class="markup-body">{bodyHtml}</div>
""";
    }

    private static string BuildHighlightedHtmlForRange(
        string fullText,
        IReadOnlyList<SuspiciousFragment> fragments,
        int rangeStart,
        int rangeEnd)
    {
        rangeStart = Math.Clamp(rangeStart, 0, fullText.Length);
        rangeEnd = Math.Clamp(rangeEnd, rangeStart, fullText.Length);
        if (rangeEnd == rangeStart)
        {
            return string.Empty;
        }

        var mapped = new List<SuspiciousFragment>();
        foreach (var f in fragments)
        {
            if (f.StartOffset < 0 || f.EndOffset <= f.StartOffset)
            {
                continue;
            }

            var s = Math.Max(f.StartOffset, rangeStart);
            var e = Math.Min(f.EndOffset, rangeEnd);
            if (e <= s)
            {
                continue;
            }

            mapped.Add(new SuspiciousFragment
            {
                FeatureKind = f.FeatureKind,
                StartOffset = s - rangeStart,
                EndOffset = e - rangeStart,
                FragmentText = fullText[s..e],
                Reason = f.Reason,
                Severity = f.Severity
            });
        }

        return BuildHighlightedHtml(fullText[rangeStart..rangeEnd], mapped);
    }

    public static string BuildHighlightedHtml(string fullText, IReadOnlyList<SuspiciousFragment> fragments)
    {
        if (string.IsNullOrEmpty(fullText))
        {
            return string.Empty;
        }

        var usable = DedupeNonOverlapping(fragments).Take(MaxFragmentsForHtml).ToList();
        if (usable.Count == 0)
        {
            return WebUtility.HtmlEncode(fullText);
        }

        var sb = new System.Text.StringBuilder(fullText.Length + usable.Count * 80);
        var cursor = 0;
        foreach (var f in usable.OrderBy(x => x.StartOffset))
        {
            if (f.StartOffset > fullText.Length)
            {
                break;
            }

            var start = Math.Clamp(f.StartOffset, 0, fullText.Length);
            var end = Math.Clamp(f.EndOffset, start, fullText.Length);
            if (start > cursor)
            {
                sb.Append(WebUtility.HtmlEncode(fullText[cursor..start]));
            }

            var token = FeatureKindMetadata.CssToken(f.FeatureKind);
            var kindValue = (int)f.FeatureKind;
            var inner = WebUtility.HtmlEncode(fullText[start..end]);
            sb.Append(
                $"<mark class=\"text-marker marker-kind-{token}\" data-kind=\"{kindValue}\" title=\"{WebUtility.HtmlEncode(f.Reason)}\">{inner}</mark>");
            cursor = end;
        }

        if (cursor < fullText.Length)
        {
            sb.Append(WebUtility.HtmlEncode(fullText[cursor..]));
        }

        return sb.ToString();
    }
}
