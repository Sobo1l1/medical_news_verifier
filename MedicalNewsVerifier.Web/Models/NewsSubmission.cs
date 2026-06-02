using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.Models;

/// <summary>
/// Уникальный ввод пользователя (заголовок + текст + ссылка), без дублирования в каждой проверке.
/// </summary>
public class NewsSubmission
{
    public int Id { get; set; }

    [MaxLength(500)]
    public string Headline { get; set; } = string.Empty;

    [MaxLength(15000)]
    public string NewsText { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    /// <summary>SHA-256 hex от нормализованного заголовка и текста.</summary>
    [MaxLength(64)]
    public string ContentFingerprint { get; set; } = string.Empty;

    public List<AnalysisRecord> AnalysisRecords { get; set; } = [];
}
