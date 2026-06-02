using System;

namespace MedicalNewsVerifier.Web.Models;

public class OfficialPublicationMatch
{
    public int Id { get; set; }

    public int AnalysisRecordId { get; set; }

    public AnalysisRecord? AnalysisRecord { get; set; }

    public int OfficialPublicationId { get; set; }

    public OfficialPublication? OfficialPublication { get; set; }

    public int RelevanceScore { get; set; }

    public DateTime MatchedAtUtc { get; set; } = DateTime.UtcNow;
}
