using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalNewsVerifier.Web.Models;

public class AnalysisRecord
{
    public int Id { get; set; }

    public int NewsSubmissionId { get; set; }

    public NewsSubmission? NewsSubmission { get; set; }

    [NotMapped]
    public string Headline => NewsSubmission?.Headline ?? string.Empty;

    [NotMapped]
    public string NewsText => NewsSubmission?.NewsText ?? string.Empty;

    [NotMapped]
    public string? SourceUrl => NewsSubmission?.SourceUrl;

    /// <summary>Итоговая (комбинированная) оценка 0–100.</summary>
    public int ReliabilityScore { get; set; }

    /// <summary>Оценка эвристики (лексика, Python, совпадения с корпусом).</summary>
    public int HeuristicReliabilityScore { get; set; }

    /// <summary>Согласованность с доверенными выдержками по мнению локальной LLM; null если анализ не выполнялся или сбой.</summary>
    public int? LlmAlignmentScore { get; set; }

    [MaxLength(8000)]
    public string? LlmSummary { get; set; }

    [NotMapped]
    public VerificationStatus Status => ComputedStatus;

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
