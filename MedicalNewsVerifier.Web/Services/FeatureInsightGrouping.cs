using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

/// <summary>
/// Группировка признаков для панели справа (например, ссылки и отсылки к источнику в одном блоке).
/// </summary>
public sealed record FeatureInsightGroup(
    string Key,
    IReadOnlyList<int> KindValues,
    string Title,
    string Description,
    IReadOnlyList<SuspiciousFragment> Fragments);

public static class FeatureInsightGrouping
{
    public const string SourceRefsKey = "source-refs";
    private const int DefaultSampleCount = 3;
    private const int SampleMaxChars = 48;

    /// <summary>До трёх коротких цитат из текста, разделённых « · ».</summary>
    public static string FormatSampleExcerpts(IEnumerable<SuspiciousFragment> fragments, int maxSamples = DefaultSampleCount)
    {
        var samples = fragments
            .Select(f => f.FragmentText?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxSamples)
            .Select(TruncateSample)
            .ToList();

        return samples.Count == 0 ? string.Empty : string.Join(" · ", samples);
    }

    private static string TruncateSample(string text) =>
        text.Length <= SampleMaxChars ? text : text[..(SampleMaxChars - 1)] + "…";

    public static IReadOnlyList<FeatureInsightGroup> BuildGroups(IEnumerable<SuspiciousFragment> fragments)
    {
        var list = fragments.Where(f => f.FeatureKind != SuspiciousFeatureKind.None).ToList();
        var result = new List<FeatureInsightGroup>();

        var sourceRefs = list
            .Where(f => f.FeatureKind is SuspiciousFeatureKind.Link or SuspiciousFeatureKind.SourceCue)
            .ToList();

        if (sourceRefs.Count > 0)
        {
            result.Add(new FeatureInsightGroup(
                SourceRefsKey,
                [(int)SuspiciousFeatureKind.Link, (int)SuspiciousFeatureKind.SourceCue],
                $"Источники и ссылки в тексте ({sourceRefs.Count})",
                "Гиперссылки и явные отсылки к ведомствам или формулировки вида «по данным …» повышают прозрачность и проверяемость.",
                sourceRefs));
        }

        foreach (var g in list
                     .Where(f => f.FeatureKind is not SuspiciousFeatureKind.Link and not SuspiciousFeatureKind.SourceCue)
                     .GroupBy(f => f.FeatureKind)
                     .OrderBy(x => (int)x.Key))
        {
            result.Add(new FeatureInsightGroup(
                $"kind-{(int)g.Key}",
                [(int)g.Key],
                $"{FeatureKindMetadata.Title(g.Key)} ({g.Count()})",
                FeatureKindMetadata.Description(g.Key),
                g.ToList()));
        }

        return result;
    }
}
