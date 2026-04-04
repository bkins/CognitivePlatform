using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class ConversationStartedEvent : TelemetryEvent
{
    public override string EventName => "Converse.Start"; 
    
    public string Input       { get; init; } = string.Empty;
    public bool   IsStreaming { get; init; } = false;

    public override string ToString()
    {
        var shortenTextBy = Input is { Length: < 25 }
                                    ? Input.Length
                                    : 25;
        var teleOutputOfUserInput = Input?.HasNoValue() ?? true
                                            ? "`request.Input` NOT provided."
                                            : Input[..shortenTextBy] + "...";
        var isStreaming = IsStreaming
                                  ? "✅"
                                  : "🚫";
        return $"{base.ToString()}\n\tInput={teleOutputOfUserInput}\n\tStreaming: {isStreaming}\n";
    }
}