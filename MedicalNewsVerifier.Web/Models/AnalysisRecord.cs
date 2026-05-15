using System.ComponentModel.DataAnnotations;

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

    /// <summary>Итоговая (комбинированная) оценка 0–100, по ней же вычисляется <see cref="Status"/>.</summary>
    public int ReliabilityScore { get; set; }

    /// <summary>Оценка эвристики (лексика, Python, совпадения с корпусом).</summary>
    public int HeuristicReliabilityScore { get; set; }

    /// <summary>Согласованность с доверенными выдержками по мнению локальной LLM; null если анализ не выполнялся или сбой.</summary>
    public int? LlmAlignmentScore { get; set; }

    [MaxLength(8000)]
    public string? LlmSummary { get; set; }

    public VerificationStatus Status { get; set; }

    [MaxLength(8000)]
    public string Explanation { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SuspiciousFragment> SuspiciousFragments { get; set; } = [];
}
