using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Integrations.Calendar;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// LLM-assisted task reasoning. Builds a context prompt from the user's active
/// tasks, optionally recent journal entries, and upcoming calendar events, then
/// delegates analysis to the LLM.
///
/// Design decisions:
/// - Calendar events (next 7 days) are included when <see cref="ICalendarProvider.IsConnected"/>
///   is true, because scheduling context ("I have back-to-back meetings tomorrow") is
///   highly relevant to task prioritisation advice.
/// - Journal context is included by default when entries exist, because cross-domain
///   reasoning (e.g. "I'm tired — which tasks should I skip today?") is the core value.
/// - The date range only applies to journal entries; tasks are always the full active list.
/// - The early-return "no data" guard only checks tasks + journal; calendar is supplementary.
/// </summary>
public class TaskReasonerActions
{
    private readonly ITaskService        _taskService;
    private readonly IJournalService     _journalService;
    private readonly ILlmClient          _llmClient;
    private readonly ICalendarProvider?  _calendar;

    public TaskReasonerActions( ITaskService       taskService
                              , IJournalService    journalService
                              , ILlmClient         llmClient
                              , ICalendarProvider? calendarProvider = null )
    {
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _llmClient      = llmClient      ?? throw new ArgumentNullException(nameof(llmClient));
        _calendar       = calendarProvider;
    }

    [NaturalLanguageAction(
            Description = "Ask a question about your tasks. The AI analyzes your active task list and recent journal entries to provide insights, recommendations, or answers."
          , Examples = new[]
                       {
                               "Which of my tasks should I focus on today?"
                             , "What are the most important things I need to do this week?"
                             , "Am I taking on too much? What should I cut?"
                             , "Given how I've been feeling, what tasks should I prioritize?"
                             , "Help me decide what to work on next."
                       }
          , Category = "tasks")]
    public async Task<string> ReasonAboutTasks(
            [NaturalLanguageParam(Description = "Your question or request about your tasks and priorities."
                                , AllowEmpty  = false)]
            string question
          , [NaturalLanguageParam(Description  = "Start date — limit journal context to entries from this date (optional)."
                                , Optional     = true
                                , DefaultValue = "")]
            string? fromDate = null
          , [NaturalLanguageParam(Description  = "End date — limit journal context to entries up to this date (optional)."
                                , Optional     = true
                                , DefaultValue = "")]
            string? toDate = null)
    {
        var tasks   = _taskService.GetActive();
        var entries = _journalService.ListEntries(ParseDate(fromDate), ParseDate(toDate));

        if (tasks.Count == 0 && entries.Count == 0)
            return "You have no active tasks and no journal entries for the specified date range.";

        var sb = new StringBuilder();
        sb.AppendLine("You are a personal productivity assistant. The user's task list and recent journal entries are provided below. Answer the user's question based on this data. Be honest if the data does not contain enough information.");
        sb.AppendLine();

        if (tasks.Count > 0)
        {
            sb.AppendLine("=== Active Tasks ===");
            foreach (var task in tasks)
            {
                sb.Append($"- {task.ShortDescription}");
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
            sb.AppendLine("=== Recent Journal Entries ===");
            // Cap journal context at 20 entries to keep prompt size reasonable
            foreach (var ewr in entries.Take(20))
            {
                sb.Append($"[{ewr.Entry.CreatedUtc:yyyy-MM-dd}] {ewr.LatestRevision.Text}");
                if (ewr.LatestRevision.Mood is not null)
                    sb.Append($" [mood: {ewr.LatestRevision.Mood}]");
                if (ewr.LatestRevision.MoodScore.HasValue)
                    sb.Append($" [mood score: {ewr.LatestRevision.MoodScore}]");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (_calendar is not null && _calendar.IsConnected)
        {
            var fromUtc = DateTimeOffset.UtcNow;
            var toUtc   = fromUtc.AddDays(7);

            var calEvents = await _calendar.GetEventsAsync(fromUtc, toUtc);
            if (calEvents.Count > 0)
            {
                sb.AppendLine("=== Upcoming Calendar Events (next 7 days) ===");
                foreach (var evt in calEvents)
                {
                    var timeLabel = evt.IsAllDay
                                         ? $"{evt.StartUtc:yyyy-MM-dd} (all day)"
                                         : $"{evt.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
                    sb.Append($"- {timeLabel} — {evt.Title}");
                    if (evt.Location is not null)
                        sb.Append($" @ {evt.Location}");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine($"User's question: {question}");

        return await _llmClient.SendAsync(sb.ToString());
    }

    private static DateTimeOffset? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return DateTimeOffset.TryParse(input, out var result) ? result : null;
    }
}
