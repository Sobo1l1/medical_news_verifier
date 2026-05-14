using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.Models;

public class OfficialPublication
{
    public int Id { get; set; }

    [MaxLength(250)]
    public string SourceName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public DateTime PublishedAtUtc { get; set; }
}
