namespace CognitivePlatform.Admin.Services.Testing;

public sealed class TestOperationsOptions
{
    public string RepositoryRoot { get; set; } = @"C:\Users\benho\source\repos";
    public string RootPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CP", "AdminTestRuns");
    public string Secret { get; set; } = string.Empty;
    public int MaxLogLines { get; set; } = 10000;
    public int MaxRetainedRuns { get; set; } = 5;
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMinutes(4);
}
