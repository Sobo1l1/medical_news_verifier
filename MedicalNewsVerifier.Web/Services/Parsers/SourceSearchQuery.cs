namespace MedicalNewsVerifier.Web.Services.Parsers;

public sealed class SourceSearchQuery
{
    public required string Headline { get; init; }
    public required string NewsText { get; init; }
    public int MaxResults { get; init; } = 5;
    public int MinRelevanceScore { get; init; } = 10;
}
