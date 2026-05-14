using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.Models;

public class SuspiciousFragment
{
    public int Id { get; set; }

    /// <summary>
    /// Классификация признака для цветовой подсветки и hover.
    /// </summary>
    public SuspiciousFeatureKind FeatureKind { get; set; }

    /// <summary>
    /// Начало фрагмента в объединённом тексте заголовка и новости (0-based). -1 если привязка к тексту не задана.
    /// </summary>
    public int StartOffset { get; set; } = -1;

    /// <summary>
    /// Конец фрагмента (индекс символа после последнего включённого).
    /// </summary>
    public int EndOffset { get; set; } = -1;

    [MaxLength(1000)]
    public string FragmentText { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Reason { get; set; } = string.Empty;

    public int Severity { get; set; }

    public int AnalysisRecordId { get; set; }

    public AnalysisRecord? AnalysisRecord { get; set; }
}
