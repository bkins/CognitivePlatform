using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// <see cref="ILoggerProvider"/> that writes every log message into <see cref="InMemoryLogStore"/>
/// so the admin Log Viewer can surface recent log activity without file I/O.
/// </summary>
public sealed class InMemoryLogProvider : ILoggerProvider
{
    private readonly InMemoryLogStore _store;

    public InMemoryLogProvider(InMemoryLogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) =>
        new InMemoryLogger(categoryName, _store);

    public void Dispose() { }
}

internal sealed class InMemoryLogger : ILogger
{
    private readonly string           _category;
    private readonly InMemoryLogStore _store;

    public InMemoryLogger(string category, InMemoryLogStore store)
    {
        _category = category;
        _store    = store;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>( LogLevel                        logLevel
                           , EventId                         eventId
                           , TState                          state
                           , Exception?                      exception
                           , Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (exception is not null)
            message += Environment.NewLine + exception;

        _store.Add(new LogEntry
                   {
                       TimestampUtc = DateTime.UtcNow
                     , Level        = ShortLevel(logLevel)
                     , Category     = _category
                     , Message      = message
                   });
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    private static string ShortLevel(LogLevel level) => level switch
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
