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

    [Url(ErrorMessage = "Некорректный URL")]
    [Display(Name = "Ссылка на публикацию")]
    public string? SourceUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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
