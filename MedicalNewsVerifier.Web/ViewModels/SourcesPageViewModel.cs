using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.ViewModels;

public class SourcesPageViewModel
{
    public List<TrustedSource> Items { get; set; } = [];
    public TrustedSourceInputModel Input { get; set; } = new();
    public TrustedSourceEditInputModel? Edit { get; set; }
}
