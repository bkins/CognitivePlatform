using System.Diagnostics;

namespace CognitivePlatform.Admin.Services.Testing;

public sealed class TestRunCoordinator : IDisposable
{
    private const string TerminalId = "test-unit-tests";
    private readonly object _gate = new();
    private readonly TestOperationsOptions _options;
    private readonly TestProjectCatalog _catalog;
    private readonly TestRunStore _store;
    private readonly ITestProcessRunner _runner;
    private readonly ITerminalStateService _terminal;
    private readonly Dictionary<Guid, TestRun> _runs;
    private readonly Func<ValidationTarget, bool, Action<string>, CancellationToken, Task<IReadOnlyList<ValidationCheck>>> _validate;
    private CancellationTokenSource? _cancellation;
    private Guid? _active;
    private bool _disposed;

    public TestRunCoordinator(TestOperationsOptions options
                            , TestProjectCatalog catalog
                            , TestRunStore store
                            , ITestProcessRunner runner
                            , ITerminalStateService terminal
                            , Func<ValidationTarget, bool, Action<string>, CancellationToken, Task<IReadOnlyList<ValidationCheck>>>? validate = null)
    {
        _options = options;
        _catalog = catalog;
        _store = store;
        _runner = runner;
        _terminal = terminal;
        _validate = validate ?? new ReadOnlyValidator(options, runner).ExecuteAsync;
        _runs = store.Load().ToDictionary(run => run.Id);
        var latest = Latest;
        if (latest is not null)
        {
            var cursor = 0;
            while (true)
            {
                var page = store.Logs(latest.Id, cursor, 500);
                foreach (var line in page.Lines) terminal.AppendLine(TerminalId, new TerminalLine(line));
                if (page.NextCursor == cursor) break;
                cursor = page.NextCursor;
            }
            terminal.SetExitCode(TerminalId, latest.Status == TestRunStatus.Passed ? 0 : 1);
        }
    }

    public IReadOnlyList<TestProfile> Profiles => _catalog.Profiles;
    public TestRun? Latest { get { lock (_gate) return _runs.Values.OrderByDescending(run => run.StartedAt).Select(_store.Clone).FirstOrDefault(); } }

    public TestRun? Get(Guid id)
    {
        lock (_gate) return _runs.TryGetValue(id, out var run) ? _store.Clone(run) : null;
    }

    public TestRun Start(TestRunRequest request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (request.RequestId == Guid.Empty) throw new ArgumentException("A nonempty requestId is required.");
            if (!Profiles.Any(profile => profile.Id == request.Profile)) throw new ArgumentException("Unknown test profile.");
            var existing = _runs.Values.SingleOrDefault(run => run.RequestId == request.RequestId);
            if (existing is not null)
            {
                if (existing.Profile != request.Profile) throw new InvalidOperationException("Request ID already belongs to a different profile.");
                return _store.Clone(existing);
            }
            if (_active.HasValue) throw new InvalidOperationException("Another test run is active.");
            var run = new TestRun
            {
                Id        = Guid.NewGuid()
              , RequestId = request.RequestId
              , Profile   = request.Profile
              , StartedAt = DateTimeOffset.UtcNow
              , Status    = TestRunStatus.Pending
              , HostBuild = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(TestRunCoordinator).Assembly)?.InformationalVersion ?? "unavailable"
              , Target    = request.Profile.StartsWith("runtime-", StringComparison.Ordinal) || request.Profile.StartsWith("deployment-", StringComparison.Ordinal) ? ValidationTarget.ForProfile(request.Profile) : null
            };
            _store.Save(run);
            _runs.Add(run.Id, run);
            _active = run.Id;
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _terminal.Clear(TerminalId);
            _terminal.MarkRunning(TerminalId, true);
            _ = Task.Run(() => ExecuteAsync(run, token));
            return _store.Clone(run);
        }
    }

    public bool Cancel(Guid id)
    {
        lock (_gate)
        {
            if (!_runs.ContainsKey(id)) return false;
            if (_active == id) _cancellation?.Cancel();
            return true;
        }
    }

    public TestLogPage Logs(Guid id
                          , int  after
                          , int  limit)
    {
        lock (_gate)
        {
            if (!_runs.ContainsKey(id)) throw new KeyNotFoundException("Test run not found.");
            return _store.Logs(id, after, limit);
        }
    }

    private async Task ExecuteAsync(TestRun run, CancellationToken token)
    {
        try
        {
            Change(run, () => run.Status = TestRunStatus.Running);
            token.ThrowIfCancellationRequested();
            var profile = Profiles.Single(profile => profile.Id == run.Profile);
            var sources = TestSourceSnapshot.Capture(_options, profile);
            if (run.Target is not null)
            {
                Change(run, () => { run.Sources = sources; run.BuildSettings = "Read-only pinned target inspection; no target process or domain-data mutation"; });
                var checks = await _validate(run.Target, run.Profile.StartsWith("deployment-", StringComparison.Ordinal), line => Log(run, line), token);
                token.ThrowIfCancellationRequested();
                Change(run, () =>
                {
                    run.Checks = checks.Select(check => check with { Name = _store.Redact(check.Name), Detail = _store.Redact(check.Detail) }).ToList();
                    run.Status = checks.Count == 0 ? TestRunStatus.NoTests
                        : checks.Any(check => check.Outcome == "TimedOut") ? TestRunStatus.TimedOut
                        : checks.All(check => check.Outcome == "Passed") ? TestRunStatus.Passed : TestRunStatus.Failed;
                });
                return;
            }
            var projects = _catalog.Discover(run.Profile);
            Change(run, () => { run.Sources = sources; run.Projects = projects.Select(path => new TestProjectResult { Project = path }).ToList(); });
            Log(run, $"Run {run.Id} Profile={run.Profile}; {projects.Count} actual test projects; {run.BuildSettings}");
            for (var index = 0; index < run.Projects.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var project = run.Projects[index];
                Log(run, $"=== Building: {Path.GetFileNameWithoutExtension(project.Project)} ===");
                var build = Command(run, project.Project, "build");
                var built = await _runner.RunAsync(build, line => Log(run, line), _options.StepTimeout, token);
                Change(run, () => project.BuildExitCode = built.ExitCode);
                if (built.TimedOut) { Change(run, () => { project.Outcome = "TimedOut"; run.Status = TestRunStatus.TimedOut; }); break; }
                if (built.ExitCode != 0) { Change(run, () => project.Outcome = "BuildFailed"); continue; }
                token.ThrowIfCancellationRequested();
                Log(run, $"=== Running tests for: {Path.GetFileNameWithoutExtension(project.Project)} ===");
                var resultDirectory = Path.Combine(_store.DirectoryFor(run.Id), "results", index.ToString());
                var test = Command(run, project.Project, "test");
                test.ArgumentList.Add("--no-build");
                test.ArgumentList.Add("--logger"); test.ArgumentList.Add("trx;LogFileName=results.trx");
                test.ArgumentList.Add("--results-directory"); test.ArgumentList.Add(resultDirectory);
                if (profile.Filter is not null) { test.ArgumentList.Add("--filter"); test.ArgumentList.Add(profile.Filter); }
                var tested = await _runner.RunAsync(test, line => Log(run, line), _options.StepTimeout, token);
                Change(run, () => project.TestExitCode = tested.ExitCode);
                if (tested.TimedOut) { Change(run, () => { project.Outcome = "TimedOut"; run.Status = TestRunStatus.TimedOut; }); break; }
                Change(run, () => TestResultReader.Read(project, Path.Combine(resultDirectory, "results.trx"), _store.Redact));
            }
            token.ThrowIfCancellationRequested();
            Change(run, () =>
            {
                if (run.Status == TestRunStatus.TimedOut) return;
                run.Status = run.Projects.Any(project => project.Outcome is "BuildFailed" or "TestFailed") ? TestRunStatus.Failed
                           : run.Projects.Count == 0 || run.Projects.Any(project => project.Outcome != "Passed") ? TestRunStatus.NoTests
                           : TestRunStatus.Passed;
            });
        }
        catch (OperationCanceledException) { Change(run, () => run.Status = _disposed ? TestRunStatus.Interrupted : TestRunStatus.Cancelled); }
        catch (Exception exception) { Change(run, () => { run.Status = TestRunStatus.Failed; run.Error = _store.Redact(exception.Message); }); }
        finally
        {
            lock (_gate)
            {
                Log(run, $"{run.Status}: {run.Passed} passed; {run.Failed} failed; {run.Skipped} skipped; build failures {run.Projects.Count(project => project.Outcome == "BuildFailed")}. RunId={run.Id}");
                run.FinishedAt = DateTimeOffset.UtcNow;
                WriteReport(run);
                _store.Save(run);
                _terminal.SetExitCode(TerminalId, run.Status == TestRunStatus.Passed ? 0 : 1);
                _terminal.MarkRunning(TerminalId, false);
                _active = null;
                _cancellation?.Dispose();
                _cancellation = null;
                _store.Trim(_runs.Values.ToArray());
                foreach (var id in _runs.Keys.Where(id => !Directory.Exists(_store.DirectoryFor(id))).ToArray()) _runs.Remove(id);
            }
        }
    }

    private ProcessStartInfo Command(TestRun run, string project, string phase)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(project)! };
        foreach (var argument in new[] { phase, project, "--artifacts-path", _store.BuildDirectoryFor(run.Id), "/p:SkipKillRunningProcesses=true", "--verbosity", "quiet" }) start.ArgumentList.Add(argument);
        start.Environment["SkipKillRunningProcesses"] = "true";
        return start;
    }

    private void Change(TestRun run, Action action)
    {
        lock (_gate) { action(); _store.Save(run); }
    }

    private void Log(TestRun run, string text)
    {
        lock (_gate)
        {
            if (_store.Append(run.Id, text)) _terminal.AppendLine(TerminalId, new TerminalLine(_store.Redact(text)));
        }
    }

    public string Summary(Guid id)
    {
        lock (_gate)
        {
            if (!_runs.ContainsKey(id)) throw new KeyNotFoundException("Test run not found.");
            var path = Path.Combine(_store.DirectoryFor(id), "summary.md");
            return File.Exists(path) ? File.ReadAllText(path) : "Run is still in progress; no final summary yet.";
        }
    }

    private void WriteReport(TestRun run)
    {
        var lines = new List<string> { $"# Test run {run.Id}", $"Profile: {run.Profile}; Status: {run.Status}", $"Passed: {run.Passed}; Failed: {run.Failed}; Skipped: {run.Skipped}" };
        foreach (var project in run.Projects) lines.Add($"- {project.Project}: {project.Outcome}; {project.Error}" + Environment.NewLine + string.Join(Environment.NewLine, project.Failures));
        foreach (var check in run.Checks) lines.Add($"- {check.Name}: {check.Outcome}; {check.Detail}");
        if (run.Target is not null) lines.Add($"Validation checks: {run.Checks.Count}; passed {run.Checks.Count(check => check.Outcome == "Passed")}. Unit-test counts above apply only to executed test projects.");
        var report = string.Join(Environment.NewLine, lines.Select(_store.Redact)) + Environment.NewLine;
        File.WriteAllText(Path.Combine(_store.DirectoryFor(run.Id), "summary.md"), report);
        var snapshot = _store.Redact(System.Text.Json.JsonSerializer.Serialize(run, TestRunStore.JsonOptions));
        var packet = new { schemaVersion = 1, runId = run.Id, result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(snapshot), markdown = report,
            reportSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(report))),
            resultSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot))) };
        File.WriteAllText(Path.Combine(_store.DirectoryFor(run.Id), "evidence.json"), System.Text.Json.JsonSerializer.Serialize(packet, TestRunStore.JsonOptions));
    }

    public string Evidence(Guid id)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(id, out var run)) throw new KeyNotFoundException("Test run not found.");
            if (!run.FinishedAt.HasValue) throw new InvalidOperationException("Evidence is available only after completion.");
            var path = Path.Combine(_store.DirectoryFor(id), "evidence.json");
            if (!File.Exists(path)) throw new KeyNotFoundException("This historical run predates evidence packets.");
            return File.ReadAllText(path);
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _cancellation?.Cancel(); }
    }
}
