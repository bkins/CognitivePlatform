using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class OrchestratorProgressEvent : TelemetryEvent
{
    public override string EventName => "Orchestrator.Progress"; 
    
    public string Stage        { get; init; } = string.Empty;
    public string StageMessage { get; init; } = string.Empty;
    public string Details      { get; init; } = string.Empty;

    public override string ToString()
    {
        var details = string.Empty;
        
        if (StageMessage.HasValue())
        {
            details = $"\n\t[{Stage}] {StageMessage}";
        }
        else if (Details.HasValue())
        {
            details = $"\n\t{Details}";
        }
        
        return $"{base.ToString()}{details}\n";
    }
}