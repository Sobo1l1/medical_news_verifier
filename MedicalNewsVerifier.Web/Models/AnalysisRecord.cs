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

    public int ReliabilityScore { get; set; }

    public VerificationStatus Status { get; set; }

    [MaxLength(4000)]
    public string Explanation { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SuspiciousFragment> SuspiciousFragments { get; set; } = [];
}
