using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Telemetry;

public interface ITelemetrySink
{
    void Track(TelemetryEvent telemetryEvent);

    void Track(string line);
}