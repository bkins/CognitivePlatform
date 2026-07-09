using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Registry.Domains;
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
[Domain(typeof(SystemDomain))]
public sealed class FeedbackActions : ISessionAware
{
    private static readonly object FileLock = new();

    private readonly BugReportSettings _settings;
    private readonly IdeaReportSettings _ideaSettings;
    private readonly ILlmRouter _llmRouter;
    private ConversationContext? _context;

    public FeedbackActions(IOptions<BugReportSettings> settings, IOptions<IdeaReportSettings> ideaSettings, ILlmRouter llmRouter)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _ideaSettings = ideaSettings?.Value ?? throw new ArgumentNullException(nameof(ideaSettings));
        _llmRouter = llmRouter ?? throw new ArgumentNullException(nameof(llmRouter));
    }

    public void SetSessionContext(ConversationContext context)
    {
        _context = context;
    }

    // ----------------------------------------------------------------------
    // ReportBug
    // ----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Logs a bug or unexpected behaviour observed during testing to the project bug log. Use when the user says 'Bug:', 'bug report:', 'found a bug:', 'report a bug:', or describes something that went wrong."
                         , Examples =
                           [
                                   "Bug: adding a task with no description crashes the app."
                                 , "Bug report: show my tasks returns an empty list even though tasks exist."
                                 , "Found a bug: the date picker accepts dates in the past without warning."
                                 , "Report a bug: typing 'journal' alone returns an error instead of asking for a description."
                           ]
                         , IsReplayable = false)]
    public string ReportBug( [NaturalLanguageParam(Description = "The full description of the bug or unexpected behaviour exactly as the user described it.")]
                             string description
                           , [NaturalLanguageParam(Description = "Optional comma-separated tags to categorise the bug (e.g. 'UI', 'performance', 'data').", Optional = true, DefaultValue = "")]
                             string? tags = null
                           , [NaturalLanguageParam(Description = "Optional severity level of the bug ('Low', 'Medium', 'High').", Optional = true, DefaultValue = "")]
                             string? severity = null
                           , [NaturalLanguageParam(Description = "Optional app state or context leading up to the bug.", Optional = true, DefaultValue = "")]
                             string? context = null)
    {
        if (description.HasNoValue())
            return "Nothing was logged — description was empty.";

        if (_settings.FilePath.HasNoValue())
            return "Bug report could not be saved: BugReport:FilePath is not configured in appsettings.";

        try
        {
            lock (FileLock)
            {
                var dir = Path.GetDirectoryName(_settings.FilePath);
                if (dir.HasValue()) Directory.CreateDirectory(dir!);

                var id = GenerateUniqueId(_settings.FilePath);
                var timestamp = DateTimeOffset.UtcNow;

                var report = new BugReport
                {
                    Id = id,
                    Timestamp = timestamp.ToString("yyyy-MM-dd HH:mm"),
                    TimeSuffix = "UTC",
                    Status = "Open",
                    Severity = string.IsNullOrWhiteSpace(severity) ? "Medium" : severity.Trim(),
                    Tags = string.IsNullOrWhiteSpace(tags) ? "None" : tags.Trim(),
                    Context = string.IsNullOrWhiteSpace(context) ? "None" : context.Trim(),
                    TriageNotes = "None",
                    Description = description.Trim()
                };

                var reports = LoadAllBugs(_settings.FilePath);
                reports.Add(report);
                SaveAllBugs(_settings.FilePath, reports);

                return $"Bug logged ✓ — added to `{Path.GetFileName(_settings.FilePath)}` with ID `{id}`.";
            }
        }
        catch (Exception ex)
        {
            return $"Bug report could not be saved: {ex.Message}";
        }
    }

    // ----------------------------------------------------------------------
    // ReportIdea
    // ----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Logs a new feature idea or suggestion to the project ideas log. Use when the user says 'Idea:', 'idea suggestion:', 'new idea:', 'suggest an idea:', or describes a suggestion/feature request."
                         , Examples =
                           [
                                   "Idea: allow sorting tasks by priority."
                                 , "New idea: add dark mode to the application."
                                 , "Suggest an idea: send a notification when a task is overdue."
                                 , "Idea suggestion: allow attaching multiple files to a journal entry."
                           ]
                         , IsReplayable = false)]
    public string ReportIdea( [NaturalLanguageParam(Description = "The full description of the idea or suggestion exactly as the user described it.")]
                              string description)
    {
        if (description.HasNoValue())
            return "Nothing was logged — description was empty.";

        if (_ideaSettings.FilePath.HasNoValue())
            return "Idea suggestion could not be saved: IdeaReport:FilePath is not configured in appsettings.";

        try
        {
            AppendIdeaToLog(description.Trim());
            return $"Idea logged ✓ — added to `{Path.GetFileName(_ideaSettings.FilePath)}`.";
        }
        catch (Exception ex)
        {
            return $"Idea suggestion could not be saved: {ex.Message}";
        }
    }

    // ----------------------------------------------------------------------
    // ListBugs
    // ----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Lists logged bugs, optionally filtered by status, severity, or tag."
                         , Examples =
                           [
                                   "List all bugs."
                                 , "Show open high severity bugs."
                                 , "List bugs with tag UI."
                           ]
                         , Category = "feedback")]
    public string ListBugs( [NaturalLanguageParam(Description = "Optional status filter (e.g. 'Open', 'Triaged', 'Resolved').", Optional = true, DefaultValue = "")]
                            string? status = null
                          , [NaturalLanguageParam(Description = "Optional severity filter (e.g. 'Low', 'Medium', 'High').", Optional = true, DefaultValue = "")]
                            string? severity = null
                          , [NaturalLanguageParam(Description = "Optional tag filter.", Optional = true, DefaultValue = "")]
                            string? tag = null )
    {
        if (_settings.FilePath.HasNoValue())
            return "BugReport:FilePath is not configured in appsettings.";

        if (!File.Exists(_settings.FilePath))
            return "No bugs have been logged yet.";

        var reports = LoadAllBugs(_settings.FilePath);

        // Apply filters
        if (!string.IsNullOrWhiteSpace(status))
        {
            reports = reports.Where(r => r.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            reports = reports.Where(r => r.Severity.Equals(severity.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tTrim = tag.Trim();
            reports = reports.Where(r => r.Tags.Split(',').Select(t => t.Trim()).Any(t => t.Equals(tTrim, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (reports.Count == 0)
            return "No matching bug reports found.";

        var sb = new StringBuilder();
        sb.AppendLine("Bug Reports:");
        foreach (var r in reports)
        {
            sb.AppendLine($"• **[ID: {r.Id}]** — {r.Timestamp}");
            sb.AppendLine($"  - **Status:** {r.Status} | **Severity:** {r.Severity} | **Tags:** {r.Tags}");
            if (r.Context != "None")
                sb.AppendLine($"  - **Context:** {r.Context}");
            if (r.TriageNotes != "None")
                sb.AppendLine($"  - **Triage Notes:** {r.TriageNotes}");
            sb.AppendLine($"  - **Description:** {r.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // TriageBug
    // ----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Triages a bug by updating its status, resolution notes, or severity."
                         , Examples =
                           [
                                   "Triage bug ABCD as status resolved with notes: fixed in next build."
                                 , "Mark bug EFGH as Triaged."
                           ]
                         , Category = "feedback")]
    public string TriageBug( [NaturalLanguageParam(Description = "The 4-character unique ID of the bug.")]
                             string id
                           , [NaturalLanguageParam(Description = "Optional new status (e.g. 'Open', 'Triaged', 'Resolved').", Optional = true, DefaultValue = "")]
                             string? status = null
                           , [NaturalLanguageParam(Description = "Optional triage/resolution notes.", Optional = true, DefaultValue = "")]
                             string? notes = null
                           , [NaturalLanguageParam(Description = "Optional new severity level.", Optional = true, DefaultValue = "")]
                             string? severity = null)
    {
        if (id.HasNoValue() || id.Length != 4)
            return "Invalid bug ID. Please provide a 4-character ID.";

        if (_settings.FilePath.HasNoValue())
            return "BugReport:FilePath is not configured in appsettings.";

        if (!File.Exists(_settings.FilePath))
            return "No bugs have been logged yet.";

        lock (FileLock)
        {
            var reports = LoadAllBugs(_settings.FilePath);
            var report = reports.FirstOrDefault(r => r.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (report is null)
                return $"No bug found with ID '{id}'.";

            var changes = new List<string>();
            if (!string.IsNullOrWhiteSpace(status))
            {
                report.Status = status.Trim();
                changes.Add($"Status -> {report.Status}");
            }
            if (!string.IsNullOrWhiteSpace(severity))
            {
                report.Severity = severity.Trim();
                changes.Add($"Severity -> {report.Severity}");
            }
            if (!string.IsNullOrWhiteSpace(notes))
            {
                report.TriageNotes = notes.Trim();
                changes.Add("Triage Notes updated");
            }

            if (changes.Count == 0)
                return $"No updates provided for bug '{id}'.";

            SaveAllBugs(_settings.FilePath, reports);

            return $"Bug '{id}' updated successfully: {string.Join(", ", changes)}.";
        }
    }

    // ----------------------------------------------------------------------
    // DeleteBug
    // ----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Deletes a bug report from the log by its 4-character ID."
                         , Examples =
                           [
                                   "Delete bug ABCD."
                                 , "Remove bug EFGH."
                           ]
                         , Category = "feedback")]
    public string DeleteBug( [NaturalLanguageParam(Description = "The 4-character unique ID of the bug to delete.")]
                             string id )
    {
        if (id.HasNoValue() || id.Length != 4)
            return "Invalid bug ID. Please provide a 4-character ID.";

        if (_settings.FilePath.HasNoValue())
            return "BugReport:FilePath is not configured in appsettings.";

        if (!File.Exists(_settings.FilePath))
            return "No bugs have been logged yet.";

        lock (FileLock)
        {
            var reports = LoadAllBugs(_settings.FilePath);
            var report = reports.FirstOrDefault(r => r.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (report is null)
                return $"No bug found with ID '{id}'.";

            reports.Remove(report);
            SaveAllBugs(_settings.FilePath, reports);

            return $"Bug '{id}' deleted successfully from `{Path.GetFileName(_settings.FilePath)}`.";
        }
    }

    // ----------------------------------------------------------------------
    // SummarizeBugs
    // ----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Uses an LLM to generate a summary of recent bug reports."
                         , Examples =
                           [
                                   "Summarize logged bugs."
                                 , "Give me a summary of recent bugs."
                           ]
                         , Category = "feedback")]
    public async Task<string> SummarizeBugs()
    {
        if (_settings.FilePath.HasNoValue())
            return "BugReport:FilePath is not configured in appsettings.";

        if (!File.Exists(_settings.FilePath))
            return "No bugs have been logged yet.";

        if (_llmRouter is null)
            return "LLM Router is not available.";

        string content;
        lock (FileLock)
        {
            content = File.ReadAllText(_settings.FilePath);
        }

        if (string.IsNullOrWhiteSpace(content) || content.StartsWith("# Bug Log") && !content.Contains("### "))
            return "There are no bugs logged to summarize.";

        var prompt = "The following is a list of user-reported bugs from a log file. "
                   + "Please generate a summary of recent bug reports, highlighting common themes, trends, and high-priority issues:\n\n"
                   + content;

        var response = await _llmRouter.SendAsync(prompt, _context ?? new ConversationContext("feedback-summary"));
        return response.Content;
    }

    // ----------------------------------------------------------------------
    // SearchBugs
    // ----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Uses natural language queries to search the bug log for relevant reports."
                         , Examples =
                           [
                                   "Search bugs for 'task list empty'."
                                 , "Find bugs related to date picker."
                           ]
                         , Category = "feedback")]
    public async Task<string> SearchBugs( [NaturalLanguageParam(Description = "The search query to match against the bug reports.")]
                                          string query )
    {
        if (query.HasNoValue())
            return "Please provide a search query.";

        if (_settings.FilePath.HasNoValue())
            return "BugReport:FilePath is not configured in appsettings.";

        if (!File.Exists(_settings.FilePath))
            return "No bugs have been logged yet.";

        if (_llmRouter is null)
            return "LLM Router is not available.";

        string content;
        lock (FileLock)
        {
            content = File.ReadAllText(_settings.FilePath);
        }

        if (string.IsNullOrWhiteSpace(content) || content.StartsWith("# Bug Log") && !content.Contains("### "))
            return "There are no bugs logged to search.";

        var prompt = $"Here is the bug log:\n\n{content}\n\n"
                   + $"Search query: \"{query}\"\n\n"
                   + "Please search the log for bug reports relevant to the query and return the most relevant entries.";

        var response = await _llmRouter.SendAsync(prompt, _context ?? new ConversationContext("feedback-search"));
        return response.Content;
    }

    // ----------------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------------

    private string GenerateShortId()
    {
        var guid = Guid.NewGuid().ToString("N");
        return guid[..4].ToUpperInvariant();
    }

    private string GenerateUniqueId(string filePath)
    {
        var existingIds = LoadExistingIds(filePath);
        string id;
        do
        {
            id = GenerateShortId();
        } while (existingIds.Contains(id));
        return id;
    }

    private HashSet<string> LoadExistingIds(string filePath)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return ids;

        var content = File.ReadAllText(filePath);
        var matches = Regex.Matches(content, @"\[ID:\s*([A-Z0-9]{4})\]");
        foreach (Match m in matches)
        {
            if (m.Groups.Count > 1)
                ids.Add(m.Groups[1].Value);
        }
        return ids;
    }

    private List<BugReport> LoadAllBugs(string filePath)
    {
        var reports = new List<BugReport>();
        if (!File.Exists(filePath)) return reports;

        var content = File.ReadAllText(filePath);
        var segments = Regex.Split(content, @"^\s*---\s*$", RegexOptions.Multiline);
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;
            if (!segment.Contains("Field Report")) continue;

            var lines = segment.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var report = new BugReport();
            var descriptionLines = new List<string>();
            bool parsingDescription = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("### 📌 Field Report") || trimmed.StartsWith("### \U0001f4cc Field Report"))
                {
                    var match = Regex.Match(trimmed, @"\[ID:\s*([A-Z0-9]{4})\]");
                    if (match.Success)
                    {
                        report.Id = match.Groups[1].Value;
                    }
                    var timeMatch = Regex.Match(trimmed, @"—\s*(.*?)\s*\((local time|UTC)\)");
                    if (timeMatch.Success)
                    {
                        report.Timestamp = timeMatch.Groups[1].Value;
                        report.TimeSuffix = timeMatch.Groups[2].Value;
                    }
                    continue;
                }

                if (trimmed.StartsWith("- **Status:**"))
                {
                    report.Status = trimmed.Substring("- **Status:**".Length).Trim();
                    continue;
                }
                if (trimmed.StartsWith("- **Severity:**"))
                {
                    report.Severity = trimmed.Substring("- **Severity:**".Length).Trim();
                    continue;
                }
                if (trimmed.StartsWith("- **Tags:**"))
                {
                    report.Tags = trimmed.Substring("- **Tags:**".Length).Trim();
                    continue;
                }
                if (trimmed.StartsWith("- **Context:**"))
                {
                    report.Context = trimmed.Substring("- **Context:**".Length).Trim();
                    continue;
                }
                if (trimmed.StartsWith("- **Triage Notes:**"))
                {
                    report.TriageNotes = trimmed.Substring("- **Triage Notes:**".Length).Trim();
                    continue;
                }

                if (parsingDescription)
                {
                    descriptionLines.Add(line);
                }
                else if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    parsingDescription = true;
                    descriptionLines.Add(line);
                }
            }

            report.Description = string.Join("\n", descriptionLines).Trim();
            if (string.IsNullOrEmpty(report.Id))
            {
                report.Id = GenerateShortId();
                report.Status = "Open";
                report.Severity = "Medium";
                report.Tags = "None";
                report.Context = "None";
                report.TriageNotes = "None";
                report.Description = segment.Trim();
            }
            reports.Add(report);
        }

        return reports;
    }

    private void SaveAllBugs(string filePath, List<BugReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Bug Log — Field Reports");
        sb.AppendLine();
        sb.AppendLine("*In-session bug reports captured via the `ReportBug` action.*");
        sb.AppendLine("*Add structured analysis and resolution notes directly in this file.*");

        foreach (var r in reports)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"### \U0001f4cc Field Report [ID: {r.Id}] — {r.Timestamp} ({r.TimeSuffix})");
            sb.AppendLine($"- **Status:** {r.Status}");
            sb.AppendLine($"- **Severity:** {r.Severity}");
            sb.AppendLine($"- **Tags:** {r.Tags}");
            sb.AppendLine($"- **Context:** {r.Context}");
            sb.AppendLine($"- **Triage Notes:** {r.TriageNotes}");
            sb.AppendLine();
            sb.AppendLine(r.Description);
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private void AppendIdeaToLog(string description)
    {
        var dir = Path.GetDirectoryName(_ideaSettings.FilePath);

        if (dir.HasValue()) Directory.CreateDirectory(dir!);

        var timestamp = DateTimeOffset.UtcNow.ToLocalTime();
        var heading   = $"### \ud83d\udca1 Raw Idea — {timestamp:yyyy-MM-dd HH:mm} (local time)";

        var entry = new StringBuilder().AppendLine()
                                       .AppendLine("---")
                                       .AppendLine()
                                       .AppendLine(heading)
                                       .AppendLine()
                                       .AppendLine(description)
                                       .ToString();

        lock (FileLock)
        {
            if (File.Exists(_ideaSettings.FilePath)
                    .Not())
            {
                var header = new StringBuilder().AppendLine("# Idea Log — Raw Ideas")
                                                .AppendLine()
                                                .AppendLine("*In-session ideas and suggestions captured via the `ReportIdea` action.*")
                                                .AppendLine("*Use these items to triage and plan future workspace enhancements.*")
                                                .ToString();

                File.WriteAllText(_ideaSettings.FilePath, header, Encoding.UTF8);
            }

            File.AppendAllText(_ideaSettings.FilePath, entry, Encoding.UTF8);
        }
    }

    private sealed class BugReport
    {
        public string Id { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string TimeSuffix { get; set; } = "UTC";
        public string Status { get; set; } = "Open";
        public string Severity { get; set; } = "Medium";
        public string Tags { get; set; } = "None";
        public string Context { get; set; } = "None";
        public string TriageNotes { get; set; } = "None";
        public string Description { get; set; } = string.Empty;
    }
}