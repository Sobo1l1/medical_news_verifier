using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.ViewModels;

public class TrustedSourceInputModel
{
    [Required(ErrorMessage = "Укажите название")]
    [MaxLength(300)]
    [Display(Name = "Название")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите URL")]
    [MaxLength(600)]
    [Url(ErrorMessage = "Введите корректный URL")]
    [Display(Name = "URL")]
    public string BaseUrl { get; set; } = string.Empty;

    [Display(Name = "Дата обращения (UTC, необязательно)")]
    public DateTime? AccessedOnUtc { get; set; }

    [Display(Name = "Включён")]
    public bool IsEnabled { get; set; } = true;
}
