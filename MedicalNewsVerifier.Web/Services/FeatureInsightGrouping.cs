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
                "Источники и ссылки в тексте",
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
                FeatureKindMetadata.Title(g.Key),
                FeatureKindMetadata.Description(g.Key),
                g.ToList()));
        }

        return result;
    }
}
