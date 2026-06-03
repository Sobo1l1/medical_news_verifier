using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public interface ISourceParser
{
    string SourceKey { get; }

    bool CanParse(TrustedSource source);

    Task<IReadOnlyList<ParsedPublication>> SearchRelevantAsync(
        TrustedSource source,
        SourceSearchQuery query,
        CancellationToken cancellationToken);
}
