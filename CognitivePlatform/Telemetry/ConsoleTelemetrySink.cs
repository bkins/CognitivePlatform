using System;

namespace CognitivePlatform.Api.Telemetry;

public class ConsoleTelemetrySink : ITelemetrySink
{
    public static string InMemoryTelemetry;
    
    public void Track(string eventName, string detail)
    {
        var currentColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        var message = $"{DateTime.Now.TimeOfDay:g} [TELE] [{eventName}] {detail}";
        InMemoryTelemetry += $"{Environment.NewLine}{message}";
        
        Console.WriteLine(message);
        
        Console.ForegroundColor = currentColor;
    }
}