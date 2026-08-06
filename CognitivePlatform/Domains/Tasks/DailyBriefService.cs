using System.Text;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Calendar;
using CognitivePlatform.Api.Wellbeing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// Pre-formats a daily brief from four independent data slices:
///
///   1. Do It Now — tasks that are both Important and Urgent (Eisenhower Q1).
///   2. Due Today or Overdue — any active task whose due date is today or earlier.
///   3. Today's Calendar — events from the connected Google Calendar for the current day.
///   4. Active Insights & Wellbeing — recent proactive patterns and health correlates.
///      The calendar section is omitted when <see cref="ICalendarProvider"/> is null,
///      <see cref="ICalendarProvider.IsConnected"/> is false, or the provider throws.
///
/// Sections 1 and 2 are always present. A task can appear in both (e.g. an Important+Urgent
/// task due today), which is a useful signal to the user.
/// </summary>
public class DailyBriefService : IDailyBriefService
{
    private readonly ITaskService                _taskService;
    private readonly ICalendarProvider?          _calendar;
    private readonly IWellbeingSignalCollector?  _wellbeingCollector;
    private readonly IWellbeingPatternService?   _wellbeingPatterns;
    private readonly IInsightHistoryStore?       _insightHistoryStore;
    private readonly ILogger<DailyBriefService>  _logger;
    private readonly EisenhowerReasoner          _eisenhower = new();

    public DailyBriefService( ITaskService                  taskService
                            , ICalendarProvider?            calendarProvider    = null
                            , IWellbeingSignalCollector?    wellbeingCollector  = null
                            , IWellbeingPatternService?     wellbeingPatterns   = null
                            , IInsightHistoryStore?         insightHistoryStore = null
                            , ILogger<DailyBriefService>?   logger              = null )
    {
        _taskService         = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _calendar            = calendarProvider;
        _wellbeingCollector  = wellbeingCollector;
        _wellbeingPatterns   = wellbeingPatterns;
        _insightHistoryStore = insightHistoryStore;
        _logger              = logger ?? NullLogger<DailyBriefService>.Instance;
    }

    // WORKSPACE NOTE (EPIC-10):
    // DailyBriefService is workspace-scoped transitively through ITaskService.
    // All task data is fetched via _taskService.GetActive(), which delegates to
    // TaskService.List() — already workspace-partitioned via IWorkspaceContext
    // (EPIC-10-B). No direct IObjectStore calls are made here; no further changes needed.

    public string GetBrief(DateOnly? localDate = null)
    {
        // BUG-12: "today" is a client concern — when the caller supplies the user's
        // local calendar date, key all date-dependent logic off that value instead of
        // the server's UTC date. Fallback preserves pre-fix behaviour.
        var effectiveDate = localDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var today         = effectiveDate.ToDateTime(TimeOnly.MinValue).Date;

        // Refresh today's wellbeing signals so pattern analysis has fresh data.
        // Runs before task/calendar work; errors are swallowed — signals being stale
        // never blocks the brief.
        WellbeingReport? wellbeingReport = null;
        if (_wellbeingCollector is not null && _wellbeingPatterns is not null)
        {
            try
            {
                _wellbeingCollector.CollectSignalsAsync(effectiveDate)
                                   .GetAwaiter()
                                   .GetResult();

                wellbeingReport = _wellbeingPatterns
                    .AnalyseAsync(effectiveDate.AddDays(-7), effectiveDate)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Wellbeing section omitted: signal collection or pattern analysis threw");
            }
        }

        var active = _taskService.GetActive();

        var eisenhower = _eisenhower.Analyze(active);

        var dueToday = active.Where(item => item.DueDate.HasValue
                                         && item.DueDate.Value.UtcDateTime.Date <= today)
                             .OrderBy(item => item.DueDate)
                             .ThenBy(item => item.CreatedAt)
                             .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"=== Daily Brief — {effectiveDate:yyyy-MM-dd} ===");

        // ---- Do It Now ----
        sb.AppendLine();
        sb.AppendLine("--- Do It Now (Important & Urgent) ---");

        if (eisenhower.DoItNow.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var task in eisenhower.DoItNow)
            {
                var due = task.DueDate.HasValue
                                  ? $" (due {task.DueDate:yyyy-MM-dd})"
                                  : string.Empty;
                sb.AppendLine($"• {task.ShortDescription}{due}");
            }
        }

        // ---- Due Today / Overdue ----
        sb.AppendLine();
        sb.AppendLine("--- Due Today or Overdue ---");

        if (dueToday.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var task in dueToday)
            {
                var isOverdue = task.DueDate!.Value.UtcDateTime.Date < today;
                var label     = isOverdue ? " [OVERDUE]" : string.Empty;
                sb.AppendLine($"• {task.ShortDescription} (due {task.DueDate:yyyy-MM-dd}){label}");
            }
        }

        // ---- Today's Calendar ----
        if (_calendar is not null && _calendar.IsConnected)
        {
            try
            {
                // Anchor the calendar window to the user's local midnight so we fetch
                // events for the user's calendar day rather than the UTC day. We assume
                // client-timezone == server-timezone (single-machine dev setup). Future
                // multi-tz deployments will need an explicit tz offset parameter.
                var localMidnight = effectiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
                var todayStart    = new DateTimeOffset(localMidnight);
                var todayEnd      = todayStart.AddDays(1);

                // Sync bridge: DailyBrief is intentionally sync; async promotion tracked in DEFERRED.md
                var rawEvents = _calendar.GetEventsAsync(todayStart, todayEnd)
                                         .GetAwaiter()
                                         .GetResult();

                // BUG-19: Google may return yesterday's all-day events when the local-midnight query
                // window starts before UTC midnight of the previous day (UTC+ timezones). Filter
                // all-day events to the user's local date only; timed events pass through unchanged.
                var calEvents = rawEvents
                    .Where(evt => !evt.IsAllDay || evt.StartUtc.LocalDateTime.Date == today)
                    .ToList();

                sb.AppendLine();
                sb.AppendLine("--- Today's Calendar ---");

                if (calEvents.Count == 0)
                {
                    sb.AppendLine("  (no events)");
                }
                else
                {
                    foreach (var evt in calEvents)
                    {
                        if (evt.IsAllDay)
                        {
                            sb.Append($"• (all day) — {evt.Title}");
                        }
                        else
                        {
                            var evtStart = evt.StartUtc.ToLocalTime();
                            var evtEnd   = evt.EndUtc.ToLocalTime();
                            sb.Append($"• {evtStart:HH:mm}–{evtEnd:HH:mm} — {evt.Title}");
                        }

                        if (evt.Location is not null)
                            sb.Append($" @ {evt.Location}");

                        if (evt.CalendarName is not null)
                            sb.Append($" [{evt.CalendarName}]");

                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Today's Calendar section omitted: calendar provider threw");
            }
        }

        // ---- Active Insights ----
        if (_insightHistoryStore is not null)
        {
            try
            {
                var recentInsights = _insightHistoryStore
                    .GetRecentAsync(TimeSpan.FromDays(3))
                    .GetAwaiter()
                    .GetResult();

                var activeInsights = recentInsights
                    .Where(item => !item.IsDeleted 
                                && item.Outcome == InsightOutcome.Unknown 
                                && !string.IsNullOrWhiteSpace(item.Message))
                    .Take(3)
                    .ToList();

                if (activeInsights.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- Active Insights ---");
                    foreach (var insight in activeInsights)
                    {
                        sb.AppendLine($"• {insight.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Active Insights section omitted: history store threw");
            }
        }

        // ---- Wellbeing Snapshot ----
        // Include one sentence when at least one Attention or Concern pattern exists;
        // omit the section entirely when patterns are Neutral/Positive or data is insufficient.
        if (wellbeingReport is not null)
        {
            var notable = wellbeingReport.Patterns
                .Where(pattern => pattern.Severity is PatternSeverity.Concern or PatternSeverity.Attention)
                .OrderByDescending(pattern => pattern.Severity)
                .FirstOrDefault();

            if (notable is not null)
            {
                sb.AppendLine();
                sb.AppendLine("--- Wellbeing ---");
                sb.AppendLine(notable.Description);
            }
        }

        return sb.ToString().TrimEnd();
    }
}
