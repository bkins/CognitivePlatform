namespace CognitivePlatform.Api.Wellbeing;

public sealed record WellbeingReport
{
    public DateOnly                        From             { get; init; }
    public DateOnly                        To               { get; init; }
    public IReadOnlyList<WellbeingPattern> Patterns         { get; init; } = [];
    public string                          NarrativeSummary { get; init; } = string.Empty;
}
