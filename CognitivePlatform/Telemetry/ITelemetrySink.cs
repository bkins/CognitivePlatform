namespace CognitivePlatform.Api.Telemetry;

public interface ITelemetrySink
{
    /// <summary>
    /// Records a simple telemetry event.
    /// Phase 1: implementations may log to console or do nothing.
    /// </summary>
    void Track(string eventName, string detail);
}