using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Telemetry;

public interface ITelemetrySink
{
    /// <summary>
    /// Records a simple telemetry event.
    /// Phase 1: implementations may log to the console or do nothing.
    /// </summary>
    //void Track(string eventName, string detail);
    void Track(TelemetryEvent telemetryEvent);

    void Track( string line );
}