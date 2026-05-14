using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.ViewModels;

public class OfficialPublicationInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите источник")]
    [MaxLength(250)]
    [Display(Name = "Название источника")]
    public string SourceName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите заголовок")]
    [MaxLength(500)]
    [Display(Name = "Заголовок")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите URL")]
    [MaxLength(500)]
    [Display(Name = "URL")]
    public string Url { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите текст для сопоставления")]
    [MaxLength(5000)]
    [Display(Name = "Текст (выдержка для поиска совпадений)")]
    public string Content { get; set; } = string.Empty;

    [Display(Name = "Дата публикации (UTC)")]
    public DateTime? PublishedAtUtc { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        Url = Url.Trim();
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult(
                "Введите полный URL с протоколом (https://…), например ссылку на оригинал материала.",
                [nameof(Url)]);
        }
    }
}
