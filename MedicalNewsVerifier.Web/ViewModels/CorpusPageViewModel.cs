using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.ViewModels;

public class CorpusPageViewModel
{
    public List<OfficialPublication> Items { get; set; } = [];
    public OfficialPublicationInputModel Input { get; set; } = new();
}
