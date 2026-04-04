using CognitivePlatform.Api.Telemetry.Events;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Telemetry;

public class ConsoleTelemetrySink : ITelemetrySink
{
    private readonly ILogger<ConsoleTelemetrySink> _logger;
    private readonly TelemetryContext              _context;

    public ConsoleTelemetrySink( ILogger<ConsoleTelemetrySink> logger
                               , TelemetryContext              context )
    {
        _logger  = logger;
        _context = context;
    }

    public void Track(TelemetryEvent telemetryEvent)
    {
        var line = telemetryEvent.ToString();
        
        Console.WriteLine(line);
    }

    public void Track( string line )
    {
        var output = line + "; TODO: Create a Telemetry Event to handle this";
        Console.WriteLine(output);
    }
}