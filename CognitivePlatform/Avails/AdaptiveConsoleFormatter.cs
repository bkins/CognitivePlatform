using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Avails;

public class AdaptiveConsoleFormatter : ConsoleFormatter
{
    private readonly SimpleConsoleFormatterOptions _options;

    public AdaptiveConsoleFormatter (IOptionsMonitor<SimpleConsoleFormatterOptions> options)
            : base("Adaptive")
    {
        _options = options.CurrentValue;
    }

    public override void Write<TState> (in LogEntry<TState>     logEntry
                                      , IExternalScopeProvider? scopeProvider
                                      , TextWriter              textWriter)
    {
        var message = logEntry.Formatter(logEntry.State
                                       , logEntry.Exception);
        if (message == null) return;

        var timestamp     = DateTime.Now.ToString(_options.TimestampFormat);
        var isDiagnostics = logEntry.Category.StartsWith("Diagnostics");

        if (isDiagnostics)
        {
            // Multi-line format for diagnostics
            textWriter.WriteLine($"{timestamp}[{logEntry.LogLevel}] {logEntry.Category}");
            textWriter.WriteLine($"    {message}");
        }
        else
        {
            // Single-line format for everything else
            textWriter.WriteLine($"{timestamp}[{logEntry.LogLevel}] {logEntry.Category}: {message}");
        }

        if (logEntry.Exception != null)
        {
            textWriter.WriteLine($"    {logEntry.Exception}");
        }
    }
}