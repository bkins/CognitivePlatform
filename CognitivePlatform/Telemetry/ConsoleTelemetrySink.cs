using System;

namespace CognitivePlatform.Api.Telemetry;

public class ConsoleTelemetrySink : ITelemetrySink
{
    public void Track(string eventName, string detail)
    {
        var currentColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        
        Console.WriteLine($"{DateTime.Now.TimeOfDay:g} [TELE] [{eventName}] {detail}");
        
        Console.ForegroundColor = currentColor;
    }
}