using System.Text;

namespace CognitivePlatform.Api.Telemetry.Events;

public class SystemControllerEvent : TelemetryEvent
{
    public override string                      EventName { get; } = "SystemController.Event";
    public          string                      Message   { get; set; }
    public          Dictionary<string, object?> Data      { get; set; }

    public override string ToString()
    {
        var data = new StringBuilder();

        foreach (var kvp in Data)
        {
            data.AppendLine($"\t\t{kvp.Key}: '{kvp.Value}'");
        }
        
        return $"{base.ToString()}\n\t{nameof(Message)}: '{Message}'\n\tData:\n{data}";
    }
}