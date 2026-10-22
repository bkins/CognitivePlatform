namespace CognitivePlatform.Admin.Services.Testing;

public sealed class TestProjectResult
{
    public string Project { get; set; } = string.Empty;
    public string Outcome { get; set; } = "Pending";
    public int? BuildExitCode { get; set; }
    public int? TestExitCode { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public string? TrxPath { get; set; }
    public List<string> Failures { get; set; } = [];
    public string? Error { get; set; }
}
