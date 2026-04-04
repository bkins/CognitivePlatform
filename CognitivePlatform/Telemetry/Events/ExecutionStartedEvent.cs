namespace CognitivePlatform.Api.Telemetry.Events;

public sealed class ExecutionStartedEvent : TelemetryEvent
{
    public override string EventName => "Execution.Start";

    public string ActionName { get; init; } = string.Empty;

    public override string ToString()
    {
        return $"{base.ToString()}\n\tActionName={ActionName}\n";
    }
}