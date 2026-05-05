namespace CognitivePlatform.Api.Domains.Feedback;

/// <summary>
/// Configuration for the in-session bug-report action.
/// Bound from the "BugReport" section of appsettings.
/// </summary>
public sealed class BugReportSettings
{
    /// <summary>
    /// Absolute path to the markdown file where field bug reports are appended.
    /// The file (and any parent directories) are created on first use if absent.
    /// Example: "C:\Users\benho\source\Application Documentation\...\Bug Log - Tech Debt.md"
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
}