using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry.Events;

public class OrchestratorCompletedEvent : TelemetryEvent
{
    public override string EventName => "Orchestrator.End";

    public string Model    { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public string Params   { get; init; } = string.Empty;

    public override string ToString()
    {
        var allProperties = string.Join(", ", Properties.Select(kv => $"{kv.Key}={kv.Value}"));
        var properties = Properties.Count != 0
                                 ? $"\n\tProperties={allProperties}\n"
                                 : "";
        
        var @params = Params.Length == 0
                              ? ""
                              : $"\n\tParams: \n\t        {Params}";
        var response = string.Empty; // = Response.Replace("\r"
        //                                 , "\n");
        
        /*
         * var shortenTextBy = Input is { Length: < 25 }
                                    ? Input.Length
                                    : 25;
        var teleOutputOfUserInput = Input?.HasNoValue() ?? true
                                            ? "`request.Input` NOT provided."
                                            : Input[..shortenTextBy] + "...";
         */
        
        var responseParts = Response.Split("\r");

        const int maxLength = 150;
        
        var shortenTextBy = responseParts[0] is { Length: < maxLength }
                                    ? responseParts[0].Length
                                    : maxLength;
        
        responseParts[0] = responseParts[0].HasNoValue()
                                    ? "`response` NOT provided."
                                    : responseParts[0][..shortenTextBy] + "..";
        
        foreach (var responsePart in responseParts)
        {
            response += $"{responsePart}; ";
        }
        var result  = $"{base.ToString()}\n\tResponse: {response}\n\t{@params}{properties}";
        
        return result;
    }
}