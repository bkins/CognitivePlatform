namespace CognitivePlatform.Api.Integrations.Health;

/// <summary>
/// A snapshot of health metrics pushed from the LAA Android app.
/// Represents data for a single calendar date.
/// </summary>
public sealed record HealthSnapshot
{
    public DateOnly Date             { get; init; }
    public long     Steps            { get; init; }
    public double   DistanceMetres   { get; init; }
    public int      AverageHeartRate { get; init; }
    public int      MinHeartRate     { get; init; }
    public int      MaxHeartRate     { get; init; }
    public int      SleepMinutes     { get; init; }
    public int      SleepSessions    { get; init; }
    public string   Platform         { get; init; } = "Android";
}
