using System.Diagnostics;
using CognitivePlatform.Admin.Services;
using CognitivePlatform.Admin.Services.Testing;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class TestRunCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cp-runs-" + Guid.NewGuid().ToString("N"));
    private readonly TerminalStateService _terminal = new();
    private readonly Mock<ITestProcessRunner> _runner = new();
    private readonly TestOperationsOptions _options;

    public TestRunCoordinatorTests()
    {
        _options = new TestOperationsOptions { RepositoryRoot = Path.Combine(_root, "repos"), RootPath = Path.Combine(_root, "runs"), Secret = "do-not-log-this", MaxLogLines = 3 };
        var path = Path.Combine(_options.RepositoryRoot, "CognitivePlatform", "Actual.Tests.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
    }

    [Theory]
    [InlineData(0, true, 0, TestRunStatus.Passed)]
    [InlineData(1, false, 0, TestRunStatus.Failed)]
    [InlineData(0, false, 0, TestRunStatus.NoTests)]
    [InlineData(0, true, 1, TestRunStatus.Failed)]
    public async Task Results_Distinguish_Build_Failure_Test_Failure_And_Missing_Results(int buildExit, bool trx, int failed, TestRunStatus expected)
    {
        _runner.Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ProcessStartInfo, Action<string>, TimeSpan, CancellationToken>((start, output, timeout, token) =>
            {
                output("diagnostic do-not-log-this");
                if (start.ArgumentList[0] == "build") return Task.FromResult(new TestProcessResult(buildExit));
                if (trx)
                {
                    var directory = start.ArgumentList[start.ArgumentList.IndexOf("--results-directory") + 1];
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "results.trx"), $"<TestRun><ResultSummary><Counters total='1' passed='{1-failed}' failed='{failed}' notExecuted='0' /></ResultSummary></TestRun>");
                }
                return Task.FromResult(new TestProcessResult(failed));
            });
        var coordinator = Build();

        var started = coordinator.Start(new TestRunRequest("unit", Guid.NewGuid()));
        var result = await Wait(coordinator, started.Id);

        Assert.Equal(expected, result.Status);
        Assert.Single(result.Projects);
        Assert.DoesNotContain("do-not-log-this", string.Join("", coordinator.Logs(started.Id, 0, 100).Lines));
        if (buildExit != 0) Assert.Equal("BuildFailed", result.Projects[0].Outcome);
        if (trx && buildExit == 0) Assert.Equal(1, result.Passed + result.Failed);
        Assert.Equal(result.Status, Build().Get(started.Id)!.Status);
    }

    [Fact]
    public async Task Admission_Is_Idempotent_SingleFlight_And_Cancel_Stops_Further_Projects()
    {
        File.WriteAllText(Path.Combine(_options.RepositoryRoot, "CognitivePlatform", "Second.Tests.csproj"), "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runner.Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ProcessStartInfo, Action<string>, TimeSpan, CancellationToken>(async (start, output, timeout, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return new TestProcessResult(0);
            });
        var coordinator = Build();
        var request = new TestRunRequest("unit", Guid.NewGuid());

        var first = coordinator.Start(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(first.Id, coordinator.Start(request).Id);
        Assert.Throws<InvalidOperationException>(() => coordinator.Start(new TestRunRequest("unit", Guid.NewGuid())));
        Assert.True(coordinator.Cancel(first.Id));
        var result = await Wait(coordinator, first.Id);

        Assert.Equal(TestRunStatus.Cancelled, result.Status);
        _runner.Verify(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Empty_Discovery_Does_Not_Report_Passed()
    {
        _options.RepositoryRoot = Path.Combine(_root, "empty");
        var coordinator = Build();

        var run = coordinator.Start(new TestRunRequest("unit", Guid.NewGuid()));

        Assert.Equal(TestRunStatus.NoTests, (await Wait(coordinator, run.Id)).Status);
    }

    [Fact]
    public void Restart_Marks_Unfinished_Evidence_Interrupted_Without_Execution()
    {
        var id = Guid.NewGuid();
        var directory = Path.Combine(_options.RootPath, id.ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "run.json"), System.Text.Json.JsonSerializer.Serialize(new TestRun { Id = id, RequestId = Guid.NewGuid(), Profile = "unit", Status = TestRunStatus.Running }));

        var coordinator = Build();

        Assert.Equal(TestRunStatus.Interrupted, coordinator.Get(id)!.Status);
        _runner.Verify(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Timeout_Stops_The_Run_Before_Any_Subsequent_Project()
    {
        File.WriteAllText(Path.Combine(_options.RepositoryRoot, "CognitivePlatform", "Second.Tests.csproj"), "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        _runner.Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new TestProcessResult(null, TimedOut: true));
        var coordinator = Build();

        var run = coordinator.Start(new TestRunRequest("unit", Guid.NewGuid()));
        var result = await Wait(coordinator, run.Id);

        Assert.Equal(TestRunStatus.TimedOut, result.Status);
        Assert.Equal("TimedOut", result.Projects[0].Outcome);
        Assert.Equal("Pending", result.Projects[1].Outcome);
        _runner.Verify(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logs_Are_Bounded_Paged_Redacted_And_Tied_To_Source_And_Isolated_Arguments()
    {
        _runner.Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<Action<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<ProcessStartInfo, Action<string>, TimeSpan, CancellationToken>((start, output, timeout, token) =>
            {
                Assert.Equal("dotnet", start.FileName);
                Assert.Contains("/p:SkipKillRunningProcesses=true", start.ArgumentList);
                Assert.Equal("true", start.Environment["SkipKillRunningProcesses"]);
                Assert.Contains("--artifacts-path", start.ArgumentList);
                for (var index = 0; index < 10; index++) output("do-not-log-this");
                return Task.FromResult(new TestProcessResult(1));
            });
        var coordinator = Build();

        var started = coordinator.Start(new TestRunRequest("unit", Guid.NewGuid()));
        var run = await Wait(coordinator, started.Id);
        var first = coordinator.Logs(run.Id, 0, 1);
        var second = coordinator.Logs(run.Id, first.NextCursor, 500);

        Assert.Equal(3, first.Lines.Length + second.Lines.Length);
        Assert.True(second.Truncated);
        Assert.DoesNotContain("do-not-log-this", string.Join("", first.Lines.Concat(second.Lines)));
        Assert.Throws<ArgumentException>(() => coordinator.Logs(run.Id, 1000, 1));
        Assert.True(run.Sources.ContainsKey("CognitivePlatform"));
        Assert.Contains("Failed", coordinator.Summary(run.Id));
    }

    [Fact]
    public void Evidence_Cannot_Be_Stored_In_Production_Or_Repository_Directories()
    {
        _options.RootPath = @"C:\CP\Data\Prod\test-runs";
        Assert.Throws<ArgumentException>(() => new TestRunStore(_options));
        _options.RootPath = _options.RepositoryRoot;
        Assert.Throws<ArgumentException>(() => new TestRunStore(_options));
    }

    [Fact]
    public async Task Retention_Deletes_Only_Owned_Completed_Run_Directories()
    {
        _options.RepositoryRoot = Path.Combine(_root, "empty");
        _options.MaxRetainedRuns = 1;
        var unrelated = Path.Combine(_root, "keep.txt");
        File.WriteAllText(unrelated, "keep");
        var coordinator = Build();
        var first = coordinator.Start(new TestRunRequest("unit", Guid.NewGuid()));
        await Wait(coordinator, first.Id);

        var second = coordinator.Start(new TestRunRequest("unit", Guid.NewGuid()));
        await Wait(coordinator, second.Id);

        Assert.Null(coordinator.Get(first.Id));
        Assert.NotNull(coordinator.Get(second.Id));
        Assert.False(Directory.Exists(Path.Combine(_options.RootPath, first.Id.ToString("N"))));
        Assert.Equal("keep", File.ReadAllText(unrelated));
    }

    private TestRunCoordinator Build() => new(_options, new TestProjectCatalog(_options), new TestRunStore(_options), _runner.Object, _terminal);

    private static async Task<TestRun> Wait(TestRunCoordinator coordinator, Guid id)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < limit)
        {
            var run = coordinator.Get(id)!;
            if (run.FinishedAt.HasValue) return run;
            await Task.Delay(10);
        }
        throw new TimeoutException("Run did not finish");
    }

    public void Dispose()
    {
        _terminal.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
