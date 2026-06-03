namespace MedicalNewsVerifier.Web.Services.Parsers;

public sealed class ParsedPublication
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public required string Url { get; init; }
    public DateTime PublishedAtUtc { get; init; }
}
