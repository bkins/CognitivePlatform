namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// A single captured log message held in the in-memory log store.
/// </summary>
public sealed record LogEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string   Level        { get; init; } = string.Empty;
    public string   Category     { get; init; } = string.Empty;
    public string   Message      { get; init; } = string.Empty;
}
