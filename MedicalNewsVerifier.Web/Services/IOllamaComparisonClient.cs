using MedicalNewsVerifier.Web;
using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

public interface IOllamaComparisonClient
{
    Task<OllamaComparisonOutcome> CompareNewsToCorpusAsync(
        string headline,
        string newsBody,
        IReadOnlyList<OfficialPublication> corpusExcerpts,
        EffectiveAnalysisRunSettings? runSettings,
        CancellationToken cancellationToken);
}

public sealed class OllamaComparisonOutcome
{
    public bool WasAttempted { get; init; }
    public bool Succeeded { get; init; }
    public int? AlignmentScore { get; init; }
    public string? Summary { get; init; }
    public string? ErrorMessage { get; init; }
}
