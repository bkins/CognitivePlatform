using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class IdempotencyHitEvent : TelemetryEvent
{
    public override string EventName { get; } = "Idempotency.Hit";

    public string ClientRequestId { get; init; } = string.Empty;

    public override string ToString()
    {
        return ClientRequestId.HasValue()
                       ? $"\n\tClientRequestId={ClientRequestId}\n"
                       : $"\n\tClientRequestId NOT provided.\n";
    }
}