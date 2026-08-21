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

