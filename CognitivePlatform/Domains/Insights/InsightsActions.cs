using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.SystemPromptLogging;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Insights;

/// <summary>
/// Cross-domain AI analysis: spans tasks, journal entries, and explicit activity events to
/// surface patterns, trends, and holistic wellbeing/productivity insights.
///
/// Design decisions:
/// - Uses the same LLM-over-data pattern as AnalyzeJournal.
/// - Tasks include completed and deleted entries so patterns like "I keep starting
///   tasks I never finish" or "I delete a lot of work tasks" are visible.
/// - Activity events (DEFERRED #4) are included as a third context section after
///   journal entries when an <see cref="IActivityLog"/> is wired in and contains
///   events for the window. Absent/empty activity data is omitted, not stubbed.
/// </summary>
public class InsightsActions
{
    private readonly ITaskService    _taskService;
    private readonly IJournalService _journalService;
    private readonly ILlmClient      _llmClient;
    private readonly IActivityLog?   _activityLog;
    private readonly IPromptLogger   _promptLogger;

    public InsightsActions( ITaskService    taskService
                          , IJournalService journalService
                          , ILlmClient      llmClient
                          , IPromptLogger   promptLogger
                          , IActivityLog?   activityLog = null )
    {
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _llmClient      = llmClient      ?? throw new ArgumentNullException(nameof(llmClient));
        _promptLogger   = promptLogger   ?? throw new ArgumentNullException(nameof(promptLogger));
        _activityLog    = activityLog;
    }

    [NaturalLanguageAction(Description = "Analyzes patterns across your tasks and journal entries using AI. Surfaces connections, trends, and holistic productivity or wellbeing insights."
                         , Examples = new[]
                                      {
                                              "What patterns do you see in my work habits?"
                                            , "Am I making progress on the things that matter most?"
                                            , "How does my mood relate to my productivity?"
                                            , "What recurring themes show up in my tasks and journal?"
                                            , "Give me an overall picture of how my week went."
                                      }
                         , Category = "insights")]
    public async Task<string> AnalyzePatterns( [NaturalLanguageParam(Description  = "What to focus the analysis on (e.g. 'productivity', 'mood', 'work-life balance', or leave blank for a general overview)."
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
        //TODO: Implement a service that does this work of aggregating and formatting the data,
        // so this action is just responsible for orchestrating the call and sending the prompt to the LLM.
        // This will also make it easier to write unit tests for the core logic without needing to mock ILlmClient.
        var from = ParseDate(fromDate);
        var to   = ParseDate(toDate);

        var tasks      = _taskService.List(from, to, includeCompleted: true);
        var entries    = _journalService.ListEntries(from, to);
        var activities = _activityLog is not null
                                 ? await _activityLog.ListAsync(from, to)
                                 : (IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>();

        if (tasks.Count == 0 
         && entries.Count == 0 
         && activities.Count == 0)
            return "No tasks, journal, or activity entries found for the specified date range.";

        var focusLabel = focus.HasValue() ? focus! : "general patterns and trends";

        var sb = new StringBuilder();
        sb.AppendLine("You are a personal productivity and wellbeing assistant. Analyze the tasks, journal entries, and logged activities below and identify patterns, trends, and actionable insights.");
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
                if (task.IsImportant)      sb.Append(" [Important]");
                if (task.IsUrgent)         sb.Append(" [Urgent]");
                if (task.DueDate.HasValue) sb.Append($" (due {task.DueDate:yyyy-MM-dd})");
                if (task.Tags.Count > 0)   sb.Append($" [tags: {string.Join(", ", task.Tags)}]");
                
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
                if (ewr.LatestRevision.Mood is not null)       sb.Append($" [mood: {ewr.LatestRevision.Mood}]");
                if (ewr.LatestRevision.MoodScore.HasValue)     sb.Append($" [mood score: {ewr.LatestRevision.MoodScore}]");
                if (ewr.LatestRevision.Tags is { Count: > 0 }) sb.Append($" [tags: {string.Join(", ", ewr.LatestRevision.Tags)}]");
                
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (activities.Count <= 0)
        {
            var promptWithoutActivities = sb.ToString();
        
            _promptLogger.Log("InsightsAnalysisPromptWithoutActivities", promptWithoutActivities, _llmClient.GetType().Name);
            return await _llmClient.SendAsync(sb.ToString());
        }

        sb.AppendLine("=== Recent Activities ===");
        // Cap at 50 events to keep prompt size reasonable
        foreach (var activityEvent in activities.Take(50))
        {
            sb.Append($"[{activityEvent.OccurredUtc:yyyy-MM-dd}] {activityEvent.ActivityType}");
            if (activityEvent.Duration.HasValue)
            {
                sb.Append($" ({activityEvent.Duration.Value}");
                if (activityEvent.Unit.HasValue()) sb.Append($" {activityEvent.Unit}");
                
                sb.Append(')');
            }
            if (activityEvent.Notes.HasValue()) sb.Append($" — {activityEvent.Notes}");
            if (activityEvent.Tags.Count > 0)   sb.Append($" [tags: {string.Join(", ", activityEvent.Tags)}]");
            
            sb.AppendLine();
        }
        sb.AppendLine();

        var promptWithActivities = sb.ToString();
        
        _promptLogger.Log("InsightsAnalysisPromptWithActivities", promptWithActivities, _llmClient.GetType().Name);
        
        return await _llmClient.SendAsync(promptWithActivities);
    }

    private static DateTimeOffset? ParseDate(string? input)
    {
        if (input?.HasNoValue() ?? true) return null;

        return DateTimeOffset.TryParse(input, out var result) ? result : null;
    }
}
