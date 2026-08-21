using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Telemetry;

public interface ITelemetryStreamService
{
    void Publish(TelemetryEvent telemetryEvent);
    IAsyncEnumerable<TelemetryEvent> SubscribeAsync(CancellationToken cancellationToken);
}
