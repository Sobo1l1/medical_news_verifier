using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.Services.Parsers;

namespace MedicalNewsVerifier.Web.Services;

public interface IOfficialSourceFetcher
{
    Task<List<OfficialPublication>> FetchRelevantAsync(
        string headline,
        string newsText,
        CancellationToken cancellationToken);
}
