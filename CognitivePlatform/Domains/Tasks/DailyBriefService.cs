using System.Text;
using CognitivePlatform.Api.Integrations.Calendar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// Pre-formats a daily brief from three independent data slices:
///
///   1. Do It Now — tasks that are both Important and Urgent (Eisenhower Q1).
///   2. Due Today or Overdue — any active task whose due date is today or earlier.
///   3. Today's Calendar — events from the connected Google Calendar for the current day.
///      The calendar section is omitted when <see cref="ICalendarProvider"/> is null,
///      <see cref="ICalendarProvider.IsConnected"/> is false, or the provider throws.
///
/// Sections 1 and 2 are always present. A task can appear in both (e.g. an Important+Urgent
/// task due today), which is a useful signal to the user.
/// </summary>
public class DailyBriefService : IDailyBriefService
{
    private readonly ITaskService               _taskService;
    private readonly ICalendarProvider?         _calendar;
    private readonly ILogger<DailyBriefService> _logger;
    private readonly EisenhowerReasoner         _eisenhower = new();

    public DailyBriefService( ITaskService                  taskService
                             , ICalendarProvider?             calendarProvider = null
                             , ILogger<DailyBriefService>?   logger           = null )
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _calendar    = calendarProvider;
        _logger      = logger ?? NullLogger<DailyBriefService>.Instance;
    }

    public string GetBrief()
    {
        var today  = DateTimeOffset.UtcNow.Date;
        var active = _taskService.GetActive();

        var eisenhower = _eisenhower.Analyze(active);

        var dueToday = active.Where(t => t.DueDate.HasValue && t.DueDate.Value.UtcDateTime.Date <= today)
                             .OrderBy(t => t.DueDate)
                             .ThenBy(t => t.CreatedAt)
                             .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"=== Daily Brief — {DateTimeOffset.UtcNow:yyyy-MM-dd} ===");

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
                var todayStart = new DateTimeOffset(today,            TimeSpan.Zero);
                var todayEnd   = new DateTimeOffset(today.AddDays(1), TimeSpan.Zero);

                // Sync bridge: DailyBrief is intentionally sync; async promotion tracked in DEFERRED.md
                var calEvents = _calendar.GetEventsAsync(todayStart, todayEnd)
                                         .GetAwaiter()
                                         .GetResult();

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

        return sb.ToString().TrimEnd();
    }
}
