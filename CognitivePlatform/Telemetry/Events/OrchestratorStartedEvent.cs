using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class OrchestratorStartedEvent : TelemetryEvent
{
    public override string EventName => "Orchestrator.Start"; 
    
    public string Model       { get; init; } = string.Empty;

    public override string ToString()
    {
        return $"{base.ToString()}\n\tModel={Model}\n";
    }
}