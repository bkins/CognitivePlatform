using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class LlmInterpreterCompletedEvent : TelemetryEvent
{
    public override string EventName => "Interpreter.End"; 
    
    public string   Output      { get; set; }  = string.Empty;
    public TimeSpan TimeElapsed { get; init; } = TimeSpan.Zero;
    
    public override string ToString()
    {
        
        return $"{base.ToString()}\n\tOutput={Output}\n\tElapsed={(int)TimeElapsed.TotalMinutes}:{TimeElapsed.Seconds:D2}.{TimeElapsed.Milliseconds:D4}\n";
    }
}