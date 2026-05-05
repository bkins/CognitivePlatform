using System.ComponentModel;
using System.Text;
using CognitivePlatform.Api.Attributes;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Domains.Feedback;

/// <summary>
/// Natural-language surface for capturing in-session bug reports.
///
/// Design decisions:
/// - No domain service layer — the action writes directly to a configured
///   markdown file. The operation is append-only and fire-and-forget; no
///   query surface is needed.
/// - File and parent directories are created on first use so the path can
///   be configured before the file physically exists.
/// - Entries are appended under a level-3 heading with a UTC timestamp so
///   the log stays human-readable in any markdown viewer.
/// - Thread-safety: a static lock guards the file append; the action class
///   is registered Transient but all instances share the same lock object.
/// </summary>
[Category("feedback")]
public sealed class FeedbackActions
{
    private static readonly object FileLock = new();

    private readonly BugReportSettings _settings;

    public FeedbackActions(IOptions<BugReportSettings> settings)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    // ----------------------------------------------------------------------
    // ReportBug
    // ----------------------------------------------------------------------

    [NaturalLanguageAction(
            Description = "Logs a bug or unexpected behaviour observed during testing to the project bug log. Use when the user says 'Bug:', 'bug report:', 'found a bug:', 'report a bug:', or describes something that went wrong."
          , Examples =
            [
                    "Bug: adding a task with no description crashes the app."
                  , "Bug report: show my tasks returns an empty list even though tasks exist."
                  , "Found a bug: the date picker accepts dates in the past without warning."
                  , "Report a bug: typing 'journal' alone returns an error instead of asking for a description."
            ]
          , IsReplayable = false)]
    public string ReportBug(
            [NaturalLanguageParam(Description = "The full description of the bug or unexpected behaviour exactly as the user described it.")]
            string description)
    {
        if (description.HasNoValue())
            return "Nothing was logged — description was empty.";

        if (_settings.FilePath.HasNoValue())
            return "Bug report could not be saved: BugReport:FilePath is not configured in appsettings.";

        try
        {
            AppendToLog(description.Trim());
            return $"Bug logged ✓ — added to {Path.GetFileName(_settings.FilePath)}.";
        }
        catch (Exception ex)
        {
            return $"Bug report could not be saved: {ex.Message}";
        }
    }

    // ----------------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------------

    private void AppendToLog(string description)
    {
        var dir = Path.GetDirectoryName(_settings.FilePath);

        if (dir.HasValue())
            Directory.CreateDirectory(dir!);

        var timestamp = DateTimeOffset.UtcNow;
        var heading   = $"### \U0001f4cc Field Report — {timestamp:yyyy-MM-dd HH:mm} UTC";

        var entry = new StringBuilder()
                   .AppendLine()
                   .AppendLine("---")
                   .AppendLine()
                   .AppendLine(heading)
                   .AppendLine()
                   .AppendLine(description)
                   .ToString();

        lock (FileLock)
        {
            // Create file with a header on first use; append on subsequent calls.
            if (File.Exists(_settings.FilePath).Not())
            {
                var header = new StringBuilder()
                            .AppendLine("# Bug Log — Field Reports")
                            .AppendLine()
                            .AppendLine("*In-session bug reports captured via the `ReportBug` action.*")
                            .AppendLine("*Add structured analysis and resolution notes directly in this file.*")
                            .ToString();

                File.WriteAllText(_settings.FilePath, header, Encoding.UTF8);
            }

            File.AppendAllText(_settings.FilePath, entry, Encoding.UTF8);
        }
    }
}