using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class ReasoningDeltaEvent : TelemetryEvent
{
    public override string EventName => "Reasoning.Delta";

    public string ReasoningDelta     { get; init; } = string.Empty;
    public int    RunningTotalTokens { get; init; }

    public override string ToString()
    {
        return $"{base.ToString()}\n\tDelta: {ReasoningDelta} (Tokens: {RunningTotalTokens})\n";
    }
}
