using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public sealed class ExecutionCompletedEvent : TelemetryEvent
{
    public override string EventName => "Execution.End";

    public string ActionName { get; init; } = string.Empty;

    public bool Success { get; init; }

    public string? Output { get; init; }

    public string? Error { get; init; }
    
    public override string ToString()
    {
        var error = Error.HasValue() ? $"\n\tError={Error}" : "";
        return $"{base.ToString()}\n\tActionName={ActionName}\n\tSuccess={Success}\n\tOutput={Output}{error}\n";
    }
}