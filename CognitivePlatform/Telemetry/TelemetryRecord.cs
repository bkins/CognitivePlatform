namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Persistent record for a single tracked telemetry event.
/// Partition key: <c>"telemetry"</c>.
/// Stored per-conversation-turn so metrics can be aggregated over any window.
/// </summary>
public sealed class TelemetryRecord
{
    public string   Id            { get; set; } = Guid.NewGuid().ToString("N");
    public string   EventName     { get; set; } = string.Empty;
    public string   SessionId     { get; set; } = string.Empty;
    public DateTime TimestampUtc  { get; set; } = DateTime.UtcNow;
    public double   DurationMs    { get; set; }
    public bool     Success       { get; set; } = true;
    public bool     IsDeleted     { get; set; }
    public DateTime? DeletedUtc   { get; set; }
}
