using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.Models;

/// <summary>
/// Справочник доверенных источников (URL и метаданные). Сопоставление по тексту идёт через <see cref="OfficialPublication"/>.
/// </summary>
public class TrustedSource
{
    public int Id { get; set; }

    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(600)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Дата обращения к источнику (для библиографии).</summary>
    public DateTime? AccessedOnUtc { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }
}
