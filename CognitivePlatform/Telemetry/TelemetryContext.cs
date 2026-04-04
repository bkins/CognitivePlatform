using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Telemetry;

public class TelemetryContext
{
    public required string SessionId { get; set; }
    public          int    Sequence  { get; private set; } = 0;

    public int NextSequence()
    {
        return ++Sequence;
    }
    public T CreateEvent<T>(T evt) where T : TelemetryEvent
    {
        evt.SessionId = SessionId;
        evt.Sequence  = NextSequence();
        
        return evt;
    }

}