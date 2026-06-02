using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace MedicalNewsVerifier.Web.Models;

public class OfficialSource
{
    public int Id { get; set; }

    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(600)]
    public string? BaseUrl { get; set; }

    public List<OfficialPublication> Publications { get; set; } = [];
}
