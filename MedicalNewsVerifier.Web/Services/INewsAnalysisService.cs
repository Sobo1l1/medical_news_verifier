using MedicalNewsVerifier.Web.Models;
using MedicalNewsVerifier.Web.ViewModels;

namespace MedicalNewsVerifier.Web.Services;

public interface INewsAnalysisService
{
    Task<(AnalysisRecord record, List<OfficialPublicationMatchVm> matches, bool isFromHistory)> AnalyzeAndSaveAsync(
        AnalyzeNewsInputModel input,
        bool forceNew,
        CancellationToken cancellationToken);

    Task<AnalysisRecord?> GetAnalysisByIdAsync(int id, CancellationToken cancellationToken);

    Task<List<OfficialPublicationMatchVm>> GetOfficialMatchesAsync(string newsText, CancellationToken cancellationToken);
}
