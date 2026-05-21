namespace CognitivePlatform.Api.Domains.Identity;

public sealed record DerivedInsight
{
    public string                Id               { get; init; } = Guid.NewGuid().ToString("N");
    public string                InsightType      { get; init; } = string.Empty;
    public string                Description      { get; init; } = string.Empty;
    public double                Confidence       { get; init; }
    public IReadOnlyList<string> SourceReferences { get; init; } = [];
    public DateTime              GeneratedAt      { get; init; } = DateTime.UtcNow;
    public bool                  UserConfirmed    { get; init; } = false;
    public bool                  IsDeleted        { get; init; } = false;
    public DateTimeOffset?       DeletedUtc       { get; init; }
}
