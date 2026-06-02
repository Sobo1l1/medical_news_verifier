using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalNewsVerifier.Web.Models;

public class OfficialPublication
{
    public int Id { get; set; }

    public int OfficialSourceId { get; set; }

    public OfficialSource? OfficialSource { get; set; }

    [NotMapped]
    public string SourceName => OfficialSource?.Name ?? string.Empty;

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    public DateTime PublishedAtUtc { get; set; }
}
