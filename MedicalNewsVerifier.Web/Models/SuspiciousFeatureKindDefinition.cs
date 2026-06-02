using System.ComponentModel.DataAnnotations;

namespace MedicalNewsVerifier.Web.Models;

public class SuspiciousFeatureKindDefinition
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(400)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(50)]
    public string CssToken { get; set; } = string.Empty;
}
