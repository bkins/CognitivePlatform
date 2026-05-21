namespace CognitivePlatform.Api.Domains.Identity;

public sealed record PersonalitySnapshot
{
    public string                Id                { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime              Timestamp         { get; init; } = DateTime.UtcNow;
    public string                NarrativeSummary  { get; init; } = string.Empty;
    public IReadOnlyList<string> DominantThemes    { get; init; } = [];
    public IReadOnlyList<string> ActiveStressors   { get; init; } = [];
    public IReadOnlyList<string> Motivators        { get; init; } = [];
    public IReadOnlyList<string> ObservedStrengths { get; init; } = [];
    public bool                  IsDeleted         { get; init; } = false;
    public DateTimeOffset?       DeletedUtc        { get; init; }
}
