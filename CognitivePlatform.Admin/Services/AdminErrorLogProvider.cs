namespace CognitivePlatform.Admin.Services;

/// <summary>
/// <see cref="ILoggerProvider"/> that forwards every Error / Critical log entry
/// from the Admin app's own <see cref="ILogger"/> pipeline into the in-memory
/// <see cref="AdminErrorLog"/>, making them visible in the Log Viewer.
/// </summary>
public sealed class AdminErrorLogProvider : ILoggerProvider
{
    private readonly AdminErrorLog _errorLog;

    public AdminErrorLogProvider(AdminErrorLog errorLog)
        => _errorLog = errorLog;

    public ILogger CreateLogger(string categoryName)
        => new AdminErrorLogger(categoryName, _errorLog);

    public void Dispose() { }
}

internal sealed class AdminErrorLogger : ILogger
{
    private readonly string        _category;
    private readonly AdminErrorLog _errorLog;

    public AdminErrorLogger(string category, AdminErrorLog errorLog)
    {
        _category = category;
        _errorLog = errorLog;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>( LogLevel                          logLevel
                            , EventId                          eventId
                            , TState                           state
                            , Exception?                       exception
                            , Func<TState, Exception?, string> formatter )
    {
        if (!IsEnabled(logLevel)) return;

        var level   = logLevel == LogLevel.Critical ? "CRT" : "ERR";
        var message = formatter(state, exception);

        _errorLog.Add( level    : level
                     , category : _category
                     , message  : message
                     , exception: exception );
    }
}
