using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.ViewModels;

public class AnalyzeNewsInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Введите заголовок новости")]
    [StringLength(500)]
    [Display(Name = "Заголовок", Description = "На русском языке")]
    public string Headline { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите текст новости")]
    [StringLength(15000, ErrorMessage = "Текст новости не должен превышать 15 000 символов. При необходимости разбейте материал на несколько проверок.")]
    [Display(Name = "Текст новости", Description = "До 15 000 символов; длинный материал можно разделить на несколько проверок. На русском языке.")]
    public string NewsText { get; set; } = string.Empty;

    [Display(Name = "Ссылка на публикацию")]
    public string? SourceUrl { get; set; }

    public bool ForceNew { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            SourceUrl = null;
        }
        else
        {
            SourceUrl = SourceUrl.Trim();
            if (!Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) ||
                string.IsNullOrEmpty(uri.Host) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                yield return new ValidationResult(
                    "Ссылка необязательна. Если указываете — введите полный адрес с протоколом (https://…).",
                    [nameof(SourceUrl)]);
            }
        }

        if (!RussianContentRules.IsPredominantlyRussian(Headline))
        {
            yield return new ValidationResult(
                "Заголовок должен быть на русском языке (основная часть букв — кириллица).",
                [nameof(Headline)]);
        }

        if (!RussianContentRules.IsPredominantlyRussian(NewsText))
        {
            yield return new ValidationResult(
                "Текст новости должен быть преимущественно на русском языке (основная часть букв — кириллица).",
                [nameof(NewsText)]);
        }
    }
}
