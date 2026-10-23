namespace CognitivePlatform.Admin.Services.Testing;

public sealed class TestRun
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string Profile { get; set; } = string.Empty;
    public TestRunStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<TestProjectResult> Projects { get; set; } = [];
    public ValidationTarget? Target { get; set; }
    public List<ValidationCheck> Checks { get; set; } = [];
    public Dictionary<string, string> Sources { get; set; } = [];
    public string HostBuild { get; set; } = "unavailable";
    public string BuildSettings { get; set; } = "Isolated artifacts; SkipKillRunningProcesses=true";
    public string? Error { get; set; }
    public int Passed => Projects.Sum(project => project.Passed);
    public int Failed => Projects.Sum(project => project.Failed);
    public int Skipped => Projects.Sum(project => project.Skipped);
}
