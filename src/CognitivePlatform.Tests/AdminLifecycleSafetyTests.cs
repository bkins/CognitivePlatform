using System.Diagnostics;
using CognitivePlatform.Admin.Services.Testing;
using ValidationPlatform.Providers.Windows;

namespace CognitivePlatform.Tests;

public sealed class AdminLifecycleSafetyTests
{
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
        worker.ArgumentList[^1] = $"$PID | Set-Content -LiteralPath '{pidFile}'; Start-Sleep -Seconds 30";
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
            Assert.DoesNotContain(Process.GetProcesses(), process => process.Id == int.Parse(File.ReadAllText(pidFile).Trim()));
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); File.Delete(pidFile); }
    }

    [Fact]
    public async Task Owned_Api_Startup_Timeout_Stops_Only_Its_Worker()
    {
        using var unrelated = Process.Start(Worker())!;
        var pidFile = Path.Combine(Path.GetTempPath(), $"cp-lifecycle-{Guid.NewGuid():N}.txt");
        var worker = Worker();
        worker.ArgumentList[^1] = $"$PID | Set-Content -LiteralPath '{pidFile}'; Start-Sleep -Seconds 30";
        try
        {
            await using var lifecycle = new ApiLifecycle(_ => { }, () => Task.FromResult<int?>(null), worker, TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<TimeoutException>(() => lifecycle.StartIfNeededAsync());
            Assert.False(unrelated.HasExited);
            Assert.True(File.Exists(pidFile));
            var pid = int.Parse(File.ReadAllText(pidFile).Trim());
            Assert.DoesNotContain(Process.GetProcesses(), process => process.Id == pid);
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
            Assert.DoesNotContain(Process.GetProcesses(), process => process.Id == id);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); }
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
