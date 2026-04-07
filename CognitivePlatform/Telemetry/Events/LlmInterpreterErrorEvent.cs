namespace CognitivePlatform.Api.Telemetry.Events;

public class LlmInterpreterErrorEvent : TelemetryEvent
{
    public override string    EventName { get; } = "Interpreter.Error";
    
    public          string    Details   { get; set; }
    public          Exception Exception { get; set; }

    public override string ToString()
    {
        return $"Error: \n\t{nameof(Details)}: '{Details}'\n\t{nameof(Exception)}: '{Exception}'\n"; 
    }
}