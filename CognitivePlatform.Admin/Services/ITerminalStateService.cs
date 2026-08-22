using System.Diagnostics;

namespace CognitivePlatform.Admin.Services;

public interface ITerminalStateService
{
    TerminalState Get(string terminalId);

    Task<int?> RunProcessAsync(
        string                             terminalId
      , ProcessStartInfo                   startInfo
      , Func<string, bool, TerminalLine>?  classify = null
      , TimeSpan?                          timeout  = null
      , CancellationToken                  ct       = default);

    void AppendLine(string terminalId, TerminalLine line);
    void MarkRunning(string terminalId, bool running, TimeSpan? timeout = null);
    void SetExitCode(string terminalId, int? exitCode);
    void SetError(string terminalId, string? error);
    void Abort(string terminalId);
    void Clear(string terminalId);

    event Action<string>? StateChanged;
}
