namespace CognitivePlatform.Api.Telemetry.Events;

public abstract class TelemetryEvent
{
    /*
     * | Field        | Why                                   |
       | ------------ | ------------------------------------- |
       | `EventId`    | Correlate / debug / deduplicate       |
       | `Sequence`   | Replay timeline in correct order      |
       | `Properties` | Add fields without breaking contracts |

     */
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public string SessionId { get; set; } = string.Empty;

    public Guid EventId { get; init; } = Guid.NewGuid();
    public int? Sequence { get; set; }

    public Dictionary<string, object?> Properties { get; set; } = new();

    public abstract string EventName { get; }

    public override string ToString()
    {
        return $"{TimestampUtc:MM/dd/yyyy hh:mm:ss.ff tt} [TELE] #{Sequence:000}  {EventName} | Session={SessionId}";
    }
    
}