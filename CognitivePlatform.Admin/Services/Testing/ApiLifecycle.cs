using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Net.Sockets;
using System.Text.Json;

namespace CognitivePlatform.Admin.Services.Testing;

/// <summary>Reuses a verified Development API, or owns only the child it starts.</summary>
public sealed class ApiLifecycle : IAsyncDisposable
{
    private readonly Action<string> _log;
    private readonly Func<Task<int?>> _probe;
    private readonly ProcessStartInfo _start;
    private readonly TimeSpan _startupTimeout;
    private Process? _owned;
    private DateTime _startedAt;
    private string? _executable;

    public ApiLifecycle(ITerminalStateService terminal, string terminalId)
        : this(line => terminal.AppendLine(terminalId, new TerminalLine(line)), ProbeDevelopmentApiAsync, DefaultStart(), TimeSpan.FromSeconds(15)) { }

    public ApiLifecycle(Action<string> log, Func<Task<int?>> probe, ProcessStartInfo start, TimeSpan startupTimeout)
    {
        _log = log;
        _probe = probe;
        _start = start;
        _startupTimeout = startupTimeout;
    }

    public async Task StartIfNeededAsync()
    {
        if (_owned is not null) throw new InvalidOperationException("API lifecycle already started.");
        if (await _probe() is { } borrowedId)
        {
            _log($"[Using verified Development API PID {borrowedId}; cleanup will preserve it.]");
            return;
        }

        _owned = Process.Start(_start) ?? throw new InvalidOperationException("API did not start.");
        _startedAt = _owned.StartTime.ToUniversalTime();
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < _startupTimeout)
            {
                if (_owned.HasExited) throw new InvalidOperationException($"Owned API exited with code {_owned.ExitCode} before readiness.");
                _owned.Refresh();
                _executable ??= GetExecutablePath(_owned);
                if (await _probe() is { } processId)
                {
                    if (processId != _owned.Id) throw new InvalidOperationException("API readiness belongs to a different process; refusing to use it.");
                    if (_executable is null) throw new InvalidOperationException("Owned API executable identity is unavailable; refusing readiness.");
                    _log($"[Owned Development API PID {processId} ready.]");
                    return;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("Development API did not become ready within the startup timeout.");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var process = _owned;
        _owned = null;
        if (process is null) return;
        using (process)
        {
            if (process.HasExited) return;
            process.Refresh();
            if (_executable is null || process.StartTime.ToUniversalTime() != _startedAt || !string.Equals(GetExecutablePath(process), _executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Owned API identity changed; refusing termination.");
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { return; }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            _log("[Owned API stopped; existing processes preserved.]");
        }
    }

    // MainModule may describe ntdll.dll while the Windows loader initializes.
    private static string? GetExecutablePath(Process process)
    {
        if (!OperatingSystem.IsWindows()) return process.MainModule?.FileName;
        var buffer = new StringBuilder(32768);
        var length = buffer.Capacity;
        return QueryFullProcessImageName(process.Handle, 0, buffer, ref length) ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);

    private static ProcessStartInfo DefaultStart()
    {
        const string executable = @"C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\bin\Debug\net10.0\CognitivePlatform.Api.exe";
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)
          , UseShellExecute = false
          , CreateNoWindow = true
        };
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add("http://0.0.0.0:5273");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        return start;
    }

    private static async Task<int?> ProbeDevelopmentApiAsync()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            using var response = await client.GetAsync("http://localhost:5273/api/system/environment");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");
            var environment = data.GetProperty("Environment");
            var database = Path.GetFullPath(environment.GetProperty("databasePath").GetString()!);
            if (data.GetProperty("Version").GetProperty("application").GetString() != "CognitivePlatform.Api"
                || environment.GetProperty("environmentName").GetString() != "Development"
                || !database.StartsWith(@"C:\CP\Data\Development\", StringComparison.OrdinalIgnoreCase)
                || environment.GetProperty("processId").GetInt32() <= 0)
                throw new InvalidOperationException("Port 5273 is not the expected Development API/data identity. Existing service preserved.");
            return environment.GetProperty("processId").GetInt32();
        }
        catch (HttpRequestException exception) when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) { return null; }
        // Invalid JSON, authentication errors, unexpected identity and timeouts fail closed.
    }
}
