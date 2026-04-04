using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class LlmInterpreterStartedEvent : TelemetryEvent
{
    public override string EventName => "Interpreter.Start"; 
    
    public string Input { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;

    public override string ToString()
    {
        var shortenTextBy = Input is { Length: < 25 }
                                    ? Input.Length
                                    : 25;
        var teleOutputOfUserInput = Input?.HasNoValue() ?? true
                                            ? "`request.Input` NOT provided."
                                            : Input[..shortenTextBy] + "...";
        
        return $"{base.ToString()}\n\tInput={teleOutputOfUserInput}\n\tModel: {Model}\n";
    }
}