using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.ViewModels;

public class TrustedSourceInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите название")]
    [MaxLength(300)]
    [Display(Name = "Название")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите URL")]
    [MaxLength(600)]
    [Display(Name = "URL")]
    public string BaseUrl { get; set; } = string.Empty;

    [Display(Name = "Дата обращения (UTC, необязательно)")]
    public DateTime? AccessedOnUtc { get; set; }

    [Display(Name = "Включён")]
    public bool IsEnabled { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        BaseUrl = BaseUrl.Trim();
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult(
                "Введите полный URL с протоколом (https://…).",
                [nameof(BaseUrl)]);
        }
    }
}
