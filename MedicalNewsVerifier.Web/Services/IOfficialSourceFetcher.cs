using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

public interface IOfficialSourceFetcher
{
    Task<List<OfficialPublication>> FetchAsync(CancellationToken cancellationToken);
}
