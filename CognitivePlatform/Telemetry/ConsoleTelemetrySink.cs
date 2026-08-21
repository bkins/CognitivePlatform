using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Telemetry;

public sealed class ConsoleTelemetrySink : ITelemetrySink
{
    public void Track(TelemetryEvent telemetryEvent)
    {
        var line = telemetryEvent.ToString();
        
        Console.WriteLine(line);
    }

    public void Track(string line)
    {
        Console.WriteLine(line);
    }
}