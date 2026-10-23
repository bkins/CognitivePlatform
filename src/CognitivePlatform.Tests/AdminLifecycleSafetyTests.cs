using System.Diagnostics;
using CognitivePlatform.Admin.Services.Testing;
using ValidationPlatform.Providers.Windows;

namespace CognitivePlatform.Tests;

public sealed class AdminLifecycleSafetyTests
{
    private static void AssertExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.True(process.HasExited, $"Owned worker {processId} is still running.");
        }
        catch (ArgumentException) { /* The terminated process object has already been released. */ }
    }

    private static ProcessStartInfo Worker()
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" }) start.ArgumentList.Add(argument);
        return start;
    }

    [Fact]
    public async Task Borrowed_Api_Is_Preserved_Without_Launching_A_Child()
    {
        using var unrelated = Process.Start(Worker())!;
        try
        {
            var lifecycle = new ApiLifecycle(_ => { }, () => Task.FromResult<int?>(unrelated.Id), new ProcessStartInfo("nonexistent-cp-worker"), TimeSpan.FromSeconds(1));
            await lifecycle.StartIfNeededAsync();
            await lifecycle.DisposeAsync();
            Assert.False(unrelated.HasExited);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); }
    }

    [Fact]
    public async Task Unrecognized_Endpoint_Fails_Without_Starting_Or_Stopping_Anything()
    {
        await using var lifecycle = new ApiLifecycle(_ => { }, () => throw new InvalidOperationException("Unexpected Production identity"), new ProcessStartInfo("nonexistent-cp-worker"), TimeSpan.FromSeconds(1));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.StartIfNeededAsync());
        Assert.Contains("Production", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owned_Readiness_Must_Match_Started_Process_And_Disposal_Preserves_Others(bool wrongIdentity)
    {
        using var unrelated = Process.Start(Worker())!;
        var pidFile = Path.Combine(Path.GetTempPath(), $"cp-readiness-{Guid.NewGuid():N}.txt");
        var worker = Worker();
        worker.ArgumentList[^1] = $"$PID | Set-Content -LiteralPath '{pidFile}.tmp'; Move-Item -LiteralPath '{pidFile}.tmp' -Destination '{pidFile}'; Start-Sleep -Seconds 30";
        var probeCount = 0;
        Task<int?> Probe()
        {
            if (++probeCount == 1 || !File.Exists(pidFile)) return Task.FromResult<int?>(null);
            var text = File.ReadAllText(pidFile).Trim();
            return Task.FromResult<int?>(int.TryParse(text, out var id) ? wrongIdentity ? unrelated.Id : id : null);
        }
        try
        {
            var lifecycle = new ApiLifecycle(_ => { }, Probe, worker, TimeSpan.FromSeconds(5));
            await using (lifecycle)
            {
                if (wrongIdentity) await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.StartIfNeededAsync());
                else await lifecycle.StartIfNeededAsync();
            }
            Assert.False(unrelated.HasExited);
            AssertExited(int.Parse(File.ReadAllText(pidFile).Trim()));
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); File.Delete(pidFile); }
    }

    [Fact]
    public async Task Owned_Api_Startup_Timeout_Stops_Only_Its_Worker()
    {
        using var unrelated = Process.Start(Worker())!;
        var pidFile = Path.Combine(Path.GetTempPath(), $"cp-lifecycle-{Guid.NewGuid():N}.txt");
        var worker = Worker();
        worker.ArgumentList[^1] = $"$PID | Set-Content -LiteralPath '{pidFile}.tmp'; Move-Item -LiteralPath '{pidFile}.tmp' -Destination '{pidFile}'; Start-Sleep -Seconds 30";
        try
        {
            await using var lifecycle = new ApiLifecycle(_ => { }, () => Task.FromResult<int?>(null), worker, TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<TimeoutException>(() => lifecycle.StartIfNeededAsync());
            Assert.False(unrelated.HasExited);
            Assert.True(File.Exists(pidFile));
            var pid = int.Parse(File.ReadAllText(pidFile).Trim());
            AssertExited(pid);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); File.Delete(pidFile); }
    }

    [Fact]
    public void Windows_Attachment_Disposal_Preserves_Borrowed_Process()
    {
        using var worker = Process.Start(Worker())!;
        try
        {
            using (var session = new WindowsProcessSession()) session.Attach(worker.Id);
            Assert.False(worker.HasExited);
        }
        finally { if (!worker.HasExited) worker.Kill(true); }
    }

    [Fact]
    public void Windows_Launch_Disposal_Stops_Only_Owned_Instance_And_Rejects_Replacement()
    {
        using var unrelated = Process.Start(Worker())!;
        try
        {
            var session = new WindowsProcessSession();
            session.Launch(Worker());
            var id = session.Process!.Id;
            Assert.Throws<InvalidOperationException>(() => session.Attach(unrelated.Id));
            session.Dispose();
            Assert.False(unrelated.HasExited);
            AssertExited(id);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); }
    }

    [Fact]
    public void Windows_Launch_Captures_Executable_Instead_Of_Transient_Loader_Module()
    {
        var sessions = new List<WindowsProcessSession>();
        var failures = new List<Exception>();
        try
        {
            for (var batch = 0; batch < 2; batch++)
            {
                sessions.Clear();
                for (var index = 0; index < 4; index++)
                {
                    var session = new WindowsProcessSession();
                    session.Launch(Worker());
                    sessions.Add(session);
                    var capturedImage = typeof(WindowsProcessSession).GetField("_executable",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(session) as string;
                    Assert.EndsWith("pwsh.exe", capturedImage, StringComparison.OrdinalIgnoreCase);
                }
                foreach (var session in sessions)
                {
                    try { session.Dispose(); }
                    catch (Exception error) { failures.Add(error); }
                }
            }
            Assert.Empty(failures);
        }
        finally
        {
            // Every session owns only its explicitly launched temporary worker.
            foreach (var session in sessions)
                if (session.Process is not null) session.Dispose();
        }
    }
    [Theory]
    [InlineData("_executable")]
    [InlineData("_startedAt")]
    public void Windows_Changed_Identity_Is_Refused_Without_Terminating_Worker(string field)
    {
        var session = new WindowsProcessSession();
        session.Launch(Worker());
        using var worker = Process.GetProcessById(session.Process!.Id);
        try
        {
            var member = typeof(WindowsProcessSession).GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            member.SetValue(session, field == "_executable" ? "unrelated.exe" : DateTime.MinValue);

            var error = Assert.Throws<InvalidOperationException>(() => session.Dispose());

            Assert.Contains("refusing termination", error.Message);
            Assert.False(worker.HasExited);
        }
        finally { if (!worker.HasExited) { worker.Kill(true); worker.WaitForExit(); } }
    }
    [Theory]
    [InlineData("_executable")]
    [InlineData("_startedAt")]
    [InlineData("missing-image")]
    public async Task Owned_Api_Changed_Or_Missing_Identity_Preserves_Worker(string identity)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        ApiLifecycle? lifecycle = null;
        var probes = 0;
        Task<int?> Probe() => Task.FromResult(++probes == 1 ? (int?)null
            : ((Process)typeof(ApiLifecycle).GetField("_owned", flags)!.GetValue(lifecycle)!).Id);
        lifecycle = new ApiLifecycle(_ => { }, Probe, Worker(), TimeSpan.FromSeconds(5));
        await lifecycle.StartIfNeededAsync();
        var owned = (Process)typeof(ApiLifecycle).GetField("_owned", flags)!.GetValue(lifecycle)!;
        using var worker = Process.GetProcessById(owned.Id);
        try
        {
            var image = typeof(ApiLifecycle).GetField("_executable", flags)!;
            Assert.EndsWith("pwsh.exe", image.GetValue(lifecycle) as string, StringComparison.OrdinalIgnoreCase);
            if (identity == "_startedAt") typeof(ApiLifecycle).GetField(identity, flags)!.SetValue(lifecycle, DateTime.MinValue);
            else image.SetValue(lifecycle, identity == "missing-image" ? null : "unrelated.exe");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.DisposeAsync().AsTask());

            Assert.Contains("refusing termination", error.Message);
            Assert.False(worker.HasExited);
        }
        finally { if (!worker.HasExited) { worker.Kill(true); worker.WaitForExit(); } }
    }

    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "CognitivePlatform", "CognitivePlatform.Admin"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    [Fact]
    public void Interactive_Admin_Runners_Do_Not_Sweep_Process_Names()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "CognitivePlatform", "CognitivePlatform.Admin", "Pages", "TestRunner.razor"));
        Assert.DoesNotContain("KillOrphanedProcesses", source);
        Assert.DoesNotContain("GetProcessesByName", source);
    }

    [Fact]
    public void Windows_Driver_Does_Not_Terminate_Preexisting_Instances()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "CP", "ValidationPlatform", "src", "ValidationPlatform.Providers.Windows", "WindowsApplicationDriver.cs"));
        Assert.DoesNotContain("GetProcessesByName", source);
        Assert.Contains("WindowsProcessSession", source);
        Assert.Contains("_session.Attach(processId)", source);
        Assert.Contains("_session.Dispose()", source);
        Assert.DoesNotContain(".Kill(", source);
    }
}
