using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Avails;

public class AdaptiveConsoleFormatter : ConsoleFormatter
{
    private readonly SimpleConsoleFormatterOptions _options;

    public const string ConsoleSummaryTitle = "Cognitive Platform API — Startup";
    
    public AdaptiveConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options)
            : base("Adaptive")
    {
        _options = options.CurrentValue;
    }

    public override void Write<TState>( in LogEntry<TState>     logEntry
                                      , IExternalScopeProvider? scopeProvider
                                      , TextWriter              textWriter )
    {
        var message = logEntry.Formatter(logEntry.State
                                       , logEntry.Exception);
        if (message is null) return;

        var timestamp     = DateTime.Now.ToString(_options.TimestampFormat);
        var level         = ToShortLevel(logEntry.LogLevel);
        var isDiagnostics = logEntry.Category.StartsWith("Diagnostics");

        var sb = new StringBuilder();

        if (isDiagnostics)
        {
            sb.AppendLine($"{timestamp}[{level}] {logEntry.Category}");

            foreach (var line in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                sb.AppendLine(ColorizeDiagnosticLine(line.TrimEnd()));
            }

            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"{timestamp}[{level}] {logEntry.Category}: {message}");
        }

        if (logEntry.Exception is not null)
        {
            sb.AppendLine($"    {logEntry.Exception}");
        }

        textWriter.Write(sb.ToString());

    }

    private static string ColorizeDiagnosticLine(string line) 
    {
        //Title
        if (line.Contains(ConsoleSummaryTitle))
        {
            return $"{Ansi.Yellow}{line}{Ansi.Reset}";
        }
        
        // Separator lines
        if (line.TrimStart().StartsWith('─'))
            return $"{Ansi.Dim}{line}{Ansi.Reset}";
    
        // URLs
        if (line.Contains("http"))
            return $"{Ansi.Green}{line}{Ansi.Reset}";

        // Section headers (no colon, not empty, not indented deeply)
        if (line.Contains(':'))
        {
            var colon = line.IndexOf(':');
            var label = line[..colon];
            var value = line[colon..];
            return $"{Ansi.Cyan}{label}{Ansi.Reset}{Ansi.White}{value}{Ansi.Reset}";
        }

        // Title line
        if (line.Contains('—'))
            return $"{Ansi.Bold}{Ansi.White}{line}{Ansi.Reset}";

        return line;
    }

    internal static class Ansi
    {
        public const string Reset  = "\u001b[0m";
        public const string Bold   = "\u001b[1m";
        public const string Dim    = "\u001b[2m";
        public const string Cyan   = "\u001b[36m";
        public const string Green  = "\u001b[32m";
        public const string White  = "\u001b[97m";
        public const string Yellow = "\u001b[33m";
    }
    
    private static string ToShortLevel( LogLevel level ) => level switch
    {
            LogLevel.Trace       => "TRC"
          , LogLevel.Debug       => "DBG"
          , LogLevel.Information => "INF"
          , LogLevel.Warning     => "WRN"
          , LogLevel.Error       => "ERR"
          , LogLevel.Critical    => "CRT"
          , _                    => "???"
    };
}