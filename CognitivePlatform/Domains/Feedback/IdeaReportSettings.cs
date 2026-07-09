namespace CognitivePlatform.Api.Domains.Feedback;

/// <summary>
/// Configuration for the in-session idea-report action.
/// Bound from the "IdeaReport" section of appsettings.
/// </summary>
public sealed class IdeaReportSettings
{
    /// <summary>
    /// Absolute path to the markdown file where feature ideas and suggestions are appended.
    /// The file (and any parent directories) are created on first use if absent.
    /// Example: "C:\Users\benho\source\Application Documentation\...\Ideas - Raw.md"
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
}
