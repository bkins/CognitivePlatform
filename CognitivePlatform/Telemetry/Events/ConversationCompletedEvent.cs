namespace CognitivePlatform.Api.Telemetry.Events;

public class ConversationCompletedEvent : TelemetryEvent
{
    public override string EventName => "Converse.Ended"; 
    
    public TimeSpan TimeElapsed { get; init; } = TimeSpan.Zero;

    public override string ToString()
    {
        return $"{base.ToString()}\n\tElapsed={(int)TimeElapsed.TotalMinutes}:{TimeElapsed.Seconds:D2}.{TimeElapsed.Milliseconds:D4}\n";
    }
}