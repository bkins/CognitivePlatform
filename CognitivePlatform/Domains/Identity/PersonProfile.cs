namespace CognitivePlatform.Api.Domains.Identity;

public sealed record PersonProfile
{
    public string                Id                       { get; init; } = "person-profile-singleton";
    public string                PreferredName            { get; init; } = string.Empty;
    public string                Occupation               { get; init; } = string.Empty;
    public IReadOnlyList<string> CoreValues               { get; init; } = [];
    public IReadOnlyList<string> PersonalityTraits        { get; init; } = [];
    public IReadOnlyList<string> LeadershipStyles         { get; init; } = [];
    public IReadOnlyList<string> LongTermGoals            { get; init; } = [];
    public IReadOnlyList<string> Strengths                { get; init; } = [];
    public IReadOnlyList<string> Stressors                { get; init; } = [];
    public IReadOnlyList<string> CommunicationPreferences { get; init; } = [];
    public string                NarrativeSummary         { get; init; } = string.Empty;
    public bool                  IsDeleted                { get; init; } = false;
    public DateTimeOffset?       DeletedUtc               { get; init; }
}
