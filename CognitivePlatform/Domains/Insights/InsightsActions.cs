using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Interpreter;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Insights;

/// <summary>
/// Cross-domain AI analysis: spans tasks and journal to surface patterns, trends,
/// and holistic wellbeing/productivity insights.
///
/// Design decisions:
/// - Uses the same LLM-over-data pattern as AnalyzeJournal.
/// - Tasks include completed and deleted entries so patterns like "I keep starting
///   tasks I never finish" or "I delete a lot of work tasks" are visible.
/// - LogActivity (explicit habit/activity logging) is deferred — see DEFERRED.md.
/// - Calendar data will be added here once calendar integration exists.
/// </summary>
public class InsightsActions
{
    private readonly ITaskService    _taskService;
    private readonly IJournalService _journalService;
    private readonly ILlmClient      _llmClient;

    public InsightsActions( ITaskService    taskService
                          , IJournalService journalService
                          , ILlmClient      llmClient )
    {
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _llmClient      = llmClient      ?? throw new ArgumentNullException(nameof(llmClient));
    }

    [NaturalLanguageAction(
            Description = "Analyzes patterns across your tasks and journal entries using AI. Surfaces connections, trends, and holistic productivity or wellbeing insights."
          , Examples = new[]
                       {
                               "What patterns do you see in my work habits?"
                             , "Am I making progress on the things that matter most?"
                             , "How does my mood relate to my productivity?"
                             , "What recurring themes show up in my tasks and journal?"
                             , "Give me an overall picture of how my week went."
                       }
          , Category = "insights")]
    public async Task<string> AnalyzePatterns(
            [NaturalLanguageParam(Description  = "What to focus the analysis on (e.g. 'productivity', 'mood', 'work-life balance', or leave blank for a general overview)."
                                , Optional     = true
                                , DefaultValue = "general patterns and trends")]
            string? focus = null
          , [NaturalLanguageParam(Description  = "Start date for the analysis window (optional)."
                                , Optional     = true
                                , DefaultValue = "")]
            string? fromDate = null
          , [NaturalLanguageParam(Description  = "End date for the analysis window (optional)."
                                , Optional     = true
                                , DefaultValue = "")]
            string? toDate = null)
    {
        var from = ParseDate(fromDate);
        var to   = ParseDate(toDate);

        var tasks   = _taskService.List(from, to, includeCompleted: true);
        var entries = _journalService.ListEntries(from, to);

        if (tasks.Count == 0 && entries.Count == 0)
            return "No tasks or journal entries found for the specified date range.";

        var focusLabel = focus.HasValue() ? focus! : "general patterns and trends";

        var sb = new StringBuilder();
        sb.AppendLine("You are a personal productivity and wellbeing assistant. Analyze the data below and identify patterns, trends, and actionable insights.");
        sb.AppendLine($"Focus area: {focusLabel}");
        sb.AppendLine();

        if (tasks.Count > 0)
        {
            sb.AppendLine("=== Tasks ===");
            foreach (var task in tasks)
            {
                var status = task.CompletedAt.HasValue ? "[Done]"
                           : task.IsDeleted            ? "[Deleted]"
                                                       : "[Active]";
                sb.Append($"{status} {task.ShortDescription}");
                if (task.IsImportant) sb.Append(" [Important]");
                if (task.IsUrgent)    sb.Append(" [Urgent]");
                if (task.DueDate.HasValue)
                    sb.Append($" (due {task.DueDate:yyyy-MM-dd})");
                if (task.Tags.Count > 0)
                    sb.Append($" [tags: {string.Join(", ", task.Tags)}]");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (entries.Count > 0)
        {
            sb.AppendLine("=== Journal Entries ===");
            // Cap journal context at 30 entries to keep prompt size reasonable
            foreach (var ewr in entries.Take(30))
            {
                sb.Append($"[{ewr.Entry.CreatedUtc:yyyy-MM-dd}] {ewr.LatestRevision.Text}");
                if (ewr.LatestRevision.Mood is not null)
                    sb.Append($" [mood: {ewr.LatestRevision.Mood}]");
                if (ewr.LatestRevision.MoodScore.HasValue)
                    sb.Append($" [mood score: {ewr.LatestRevision.MoodScore}]");
                if (ewr.LatestRevision.Tags is { Count: > 0 })
                    sb.Append($" [tags: {string.Join(", ", ewr.LatestRevision.Tags)}]");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return await _llmClient.SendAsync(sb.ToString());
    }

    private static DateTimeOffset? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return DateTimeOffset.TryParse(input, out var result) ? result : null;
    }
}
