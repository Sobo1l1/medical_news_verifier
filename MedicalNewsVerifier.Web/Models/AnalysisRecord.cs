using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalNewsVerifier.Web.Models;

public class AnalysisRecord
{
    public int Id { get; set; }

    [MaxLength(500)]
    public string Headline { get; set; } = string.Empty;

    [MaxLength(15000)]
    public string NewsText { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    /// <summary>Итоговая (комбинированная) оценка 0–100, по которой определяется статус.</summary>
    public int ReliabilityScore { get; set; }

    /// <summary>Оценка эвристики (лексика, Python, совпадения с корпусом).</summary>
    public int HeuristicReliabilityScore { get; set; }

    /// <summary>Согласованность с доверенными выдержками по мнению локальной LLM; null если анализ не выполнялся или сбой.</summary>
    public int? LlmAlignmentScore { get; set; }

    [MaxLength(8000)]
    public string? LlmSummary { get; set; }

    /// <summary>
    /// Статус проверки, сохраняемый в БД (нужен для истории/экспорта).
    /// </summary>
    public VerificationStatus Status { get; set; } = VerificationStatus.NeedsReview;

    [NotMapped]
    public VerificationStatus ComputedStatus => ReliabilityScore switch
    {
        >= 70 => VerificationStatus.LikelyReliable,
        <= 40 => VerificationStatus.Suspicious,
        _ => VerificationStatus.NeedsReview
    };

    [MaxLength(8000)]
    public string Explanation { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SuspiciousFragment> SuspiciousFragments { get; set; } = [];

    public List<OfficialPublicationMatch> OfficialPublicationMatches { get; set; } = [];
}
