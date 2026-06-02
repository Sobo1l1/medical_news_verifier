using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.ViewModels;

public class TrustedSourceEditInputModel : TrustedSourceInputModel
{
    public int Id { get; set; }

    [Display(Name = "Порядок сортировки")]
    public int SortOrder { get; set; }
}
