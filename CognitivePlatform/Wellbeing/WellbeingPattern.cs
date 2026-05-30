namespace CognitivePlatform.Api.Wellbeing;

public sealed record WellbeingPattern
{
    public string                  Name        { get; init; } = string.Empty;
    public string                  Description { get; init; } = string.Empty;
    public WellbeingSignalSource[] Sources     { get; init; } = [];
    public PatternSeverity         Severity    { get; init; }
    public double?                 Correlation { get; init; }
}
