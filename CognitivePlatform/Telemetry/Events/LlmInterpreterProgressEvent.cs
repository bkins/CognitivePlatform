using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class LlmInterpreterProgressEvent : TelemetryEvent
{
    public override string EventName => "LlmInterpreter.Progress"; 
    
    public string Details       { get; init; } = string.Empty;

    public override string ToString()
    {
        var details = string.Empty;
        
        if (Details.HasValue())
        {
            details = $"\n\t{Details}";
        }
        
        return $"{base.ToString()}{details}\n";
    }
}