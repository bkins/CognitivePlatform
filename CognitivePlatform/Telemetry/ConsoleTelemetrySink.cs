using System;

namespace CognitivePlatform.Api.Telemetry;

public class ConsoleTelemetrySink : ITelemetrySink
{
    public void Track(string eventName, string detail)
    {
        Console.WriteLine($"[{eventName}] {detail}");
    }
}