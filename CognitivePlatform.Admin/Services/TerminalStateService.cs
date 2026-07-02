using System.Collections.Concurrent;
using System.Diagnostics;

namespace CognitivePlatform.Admin.Services;

public sealed class TerminalStateService : ITerminalStateService, IDisposable
{
    private sealed record Context( TerminalState              State
                                 , Process?                   Process
                                 , CancellationTokenSource?   Cts);

    private readonly ConcurrentDictionary<string, Context> _contexts = new();
    private readonly Timer _tick;

    public event Action<string>? StateChanged;

    public TerminalStateService()
    {
        _tick = new Timer(OnTick, null
                        , TimeSpan.FromSeconds(1)
                        , TimeSpan.FromSeconds(1));
    }

    private void OnTick(object? _)
    {
        foreach (var (id, ctx) in _contexts)
        {
            if (ctx.State.IsRunning)
                StateChanged?.Invoke(id);
        }
    }

    public TerminalState Get(string terminalId)
        => _contexts.GetOrAdd(terminalId, _ => new Context(new TerminalState(), null, null)).State;

    public async Task<int?> RunProcessAsync( string                            terminalId
                                           , ProcessStartInfo                  startInfo
                                           , Func<string, bool, TerminalLine>? classify = null
                                           , CancellationToken                 ct       = default)
    {
        KillExisting(terminalId);

        var state   = Get(terminalId);
        var cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var process = new Process { StartInfo = startInfo };

        _contexts[terminalId] = new Context(state, process, cts);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var line = classify is not null
                ? classify(e.Data, false)
                : new TerminalLine(e.Data, TerminalLineKind.Normal);
            state.Append(line);
            StateChanged?.Invoke(terminalId);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var line = classify is not null
                ? classify(e.Data, true)
                : new TerminalLine(e.Data, TerminalLineKind.Error);
            state.Append(line);
            StateChanged?.Invoke(terminalId);
        };

        int? exitCode = null;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cts.Token);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            state.Append(new TerminalLine("[Aborted by user]", TerminalLineKind.Error));
            StateChanged?.Invoke(terminalId);
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
            state.Append(new TerminalLine($"[Error: {ex.Message}]", TerminalLineKind.Error));
            StateChanged?.Invoke(terminalId);
        }
        finally
        {
            process.Dispose();
            cts.Dispose();
            _contexts[terminalId] = new Context(state, null, null);
        }

        return exitCode;
    }

    public void AppendLine(string terminalId, TerminalLine line)
    {
        Get(terminalId).Append(line);
        StateChanged?.Invoke(terminalId);
    }

    public void MarkRunning(string terminalId, bool running)
    {
        var state = Get(terminalId);
        state.IsRunning = running;
        state.StartedAt = running ? DateTime.UtcNow : null;
        StateChanged?.Invoke(terminalId);
    }

    public void SetExitCode(string terminalId, int? exitCode)
    {
        Get(terminalId).ExitCode = exitCode;
        StateChanged?.Invoke(terminalId);
    }

    public void SetError(string terminalId, string? error)
    {
        Get(terminalId).Error = error;
        StateChanged?.Invoke(terminalId);
    }

    public void Abort(string terminalId)
    {
        KillExisting(terminalId);
        MarkRunning(terminalId, false);
    }

    public void Clear(string terminalId)
    {
        KillExisting(terminalId);
        Get(terminalId).Reset();
        StateChanged?.Invoke(terminalId);
    }

    private void KillExisting(string terminalId)
    {
        if (!_contexts.TryGetValue(terminalId, out var ctx)) return;
        ctx.Cts?.Cancel();
        try { ctx.Process?.Kill(entireProcessTree: true); }
        catch { /* already exited */ }
    }

    public void Dispose()
    {
        _tick.Dispose();
        foreach (var (_, ctx) in _contexts)
        {
            ctx.Cts?.Cancel();
            try { ctx.Process?.Kill(entireProcessTree: true); }
            catch { }
            ctx.Process?.Dispose();
            ctx.Cts?.Dispose();
        }
    }
}
