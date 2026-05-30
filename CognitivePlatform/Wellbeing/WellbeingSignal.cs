namespace CognitivePlatform.Api.Wellbeing;

/// <summary>
/// A single dated signal captured from one data source. Multiple signals are stored
/// per calendar day, one per (Source, MetricName) pair. Key format used by
/// <see cref="WellbeingSignalStore"/>: <c>"wellbeing.signal.{yyyy-MM-dd}"</c>.
/// </summary>
public sealed record WellbeingSignal
{
    /// <summary>Stable key: <c>"{Source}.{MetricName}.{Date:yyyy-MM-dd}"</c>. Used as the IObjectStore id.</summary>
    public string              Id           { get; init; } = string.Empty;
    public DateOnly            Date         { get; init; }
    public WellbeingSignalSource Source     { get; init; }
    public string              MetricName   { get; init; } = string.Empty;
    public double              Value        { get; init; }
    public string?             Unit         { get; init; }
    public DateTimeOffset      CollectedAt  { get; init; } = DateTimeOffset.UtcNow;
    public bool                IsDeleted    { get; init; }
    public DateTimeOffset?     DeletedUtc   { get; init; }
}
