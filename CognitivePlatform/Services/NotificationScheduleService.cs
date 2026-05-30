using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Services;

/// <summary>
/// Rule-based implementation of <see cref="INotificationScheduleProvider"/> for Phase N.1.
/// Evaluates current state across Tasks, DailyRecord, and Journal and returns only
/// the reminders whose conditions are currently unmet. Guard rules (max-per-day,
/// min-gap, quiet hours) are applied before returning.
/// </summary>
public sealed class NotificationScheduleService : INotificationScheduleProvider
{
    private readonly ITaskService                           _taskService;
    private readonly IDailyRecordService                    _dailyRecordService;
    private readonly IJournalService                        _journalService;
    private readonly NotificationSettings                   _settings;
    private readonly ILogger<NotificationScheduleService>   _logger;

    public NotificationScheduleService( ITaskService                             taskService
                                      , IDailyRecordService                       dailyRecordService
                                      , IJournalService                           journalService
                                      , IOptions<NotificationSettings>            settings
                                      , ILogger<NotificationScheduleService>?     logger = null )
    {
        _taskService        = taskService        ?? throw new ArgumentNullException(nameof(taskService));
        _dailyRecordService = dailyRecordService ?? throw new ArgumentNullException(nameof(dailyRecordService));
        _journalService     = journalService     ?? throw new ArgumentNullException(nameof(journalService));
        _settings           = settings?.Value    ?? new NotificationSettings();
        _logger             = logger             ?? NullLogger<NotificationScheduleService>.Instance;
    }

    public Task<NotificationSchedule> GetScheduleAsync(DateTimeOffset from, CancellationToken ct = default)
    {
        try
        {
            var localFrom = from.ToLocalTime();
            var today     = DateOnly.FromDateTime(localFrom.DateTime);
            var tomorrow  = today.AddDays(1);

            var candidates = new List<ScheduledNotification>();

            BuildDayOpenCandidate(from, today, candidates);
            BuildDayCloseCandidate(from, today, localFrom, candidates);
            BuildJournalCandidate(from, today, candidates);
            BuildTaskDueCandidates(from, today, tomorrow, candidates);

            var ordered  = candidates.OrderBy(notification => notification.FireAt).ToList();
            var filtered = ApplyGuardRules(ordered);

            return Task.FromResult(new NotificationSchedule { Notifications = filtered });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "NotificationScheduleService failed to build schedule");
            return Task.FromResult(new NotificationSchedule());
        }
    }

    private void BuildDayOpenCandidate( DateTimeOffset              from
                                      , DateOnly                    today
                                      , List<ScheduledNotification> candidates )
    {
        var todayRecord = _dailyRecordService.GetToday();
        if (todayRecord is not null && todayRecord.OpenedAtUtc is not null)
            return;

        var fireAt = ToLocalDateTimeOffset(today, new TimeOnly(8, 0));
        if (fireAt <= from) return;

        candidates.Add(new ScheduledNotification
                        {
                            ExternalId = $"day-open-{today:yyyy-MM-dd}"
                          , Title      = "Open Your Day"
                          , Body       = "You haven’t opened your day yet. Ready to set your intentions?"
                          , FireAt     = fireAt
                          , Category   = NotificationCategory.DayOpen
                        });
    }

    private void BuildDayCloseCandidate( DateTimeOffset              from
                                       , DateOnly                    today
                                       , DateTimeOffset              localFrom
                                       , List<ScheduledNotification> candidates )
    {
        if (localFrom.Hour < 20) return;

        var todayRecord = _dailyRecordService.GetToday();
        if (todayRecord is null || todayRecord.OpenedAtUtc is null || todayRecord.ClosedAtUtc is not null)
            return;

        var fireAt = ToLocalDateTimeOffset(today, new TimeOnly(21, 30));
        if (fireAt <= from) return;

        candidates.Add(new ScheduledNotification
                        {
                            ExternalId = $"day-close-{today:yyyy-MM-dd}"
                          , Title      = "Close Your Day"
                          , Body       = "You opened your day but haven’t closed it yet. Take a moment to reflect."
                          , FireAt     = fireAt
                          , Category   = NotificationCategory.DayClose
                        });
    }

    private void BuildJournalCandidate( DateTimeOffset              from
                                      , DateOnly                    today
                                      , List<ScheduledNotification> candidates )
    {
        var dayStartUtc = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)).ToUniversalTime();
        var dayEndUtc   = dayStartUtc.AddDays(1);
        var entries     = _journalService.ListEntries(dayStartUtc, dayEndUtc);
        if (entries.Count > 0) return;

        var fireAt = ToLocalDateTimeOffset(today, new TimeOnly(21, 0));
        if (fireAt <= from) return;

        candidates.Add(new ScheduledNotification
                        {
                            ExternalId = $"journal-{today:yyyy-MM-dd}"
                          , Title      = "Journal Check-In"
                          , Body       = "You haven’t journaled today. A moment of reflection goes a long way."
                          , FireAt     = fireAt
                          , Category   = NotificationCategory.Journal
                        });
    }

    private void BuildTaskDueCandidates( DateTimeOffset              from
                                       , DateOnly                    today
                                       , DateOnly                    tomorrow
                                       , List<ScheduledNotification> candidates )
    {
        var activeTasks = _taskService.GetActive();

        foreach (var task in activeTasks)
        {
            if (task.DueDate is null) continue;

            var dueDateLocal = DateOnly.FromDateTime(task.DueDate.Value.ToLocalTime().DateTime);

            if (dueDateLocal == today)
            {
                var fireAt = ToLocalDateTimeOffset(today, new TimeOnly(8, 30));
                if (fireAt > from)
                {
                    candidates.Add(new ScheduledNotification
                                    {
                                        ExternalId = $"task-due-today-{task.Id}"
                                      , Title      = "Task Due Today"
                                      , Body       = $"“{task.ShortDescription}” is due today."
                                      , FireAt     = fireAt
                                      , Category   = NotificationCategory.TaskDue
                                    });
                }
            }
            else if (dueDateLocal == tomorrow)
            {
                var fireAt = ToLocalDateTimeOffset(today, new TimeOnly(19, 0));
                if (fireAt > from)
                {
                    candidates.Add(new ScheduledNotification
                                    {
                                        ExternalId = $"task-due-tomorrow-{task.Id}"
                                      , Title      = "Task Due Tomorrow"
                                      , Body       = $"“{task.ShortDescription}” is due tomorrow."
                                      , FireAt     = fireAt
                                      , Category   = NotificationCategory.TaskDue
                                    });
                }
            }
        }
    }

    private IReadOnlyList<ScheduledNotification> ApplyGuardRules(List<ScheduledNotification> candidates)
    {
        var result   = new List<ScheduledNotification>();
        DateTimeOffset? lastKept = null;

        foreach (var candidate in candidates)
        {
            if (result.Count >= _settings.MaxPerDay) break;

            if (IsInQuietHours(candidate.FireAt.ToLocalTime().Hour)) continue;

            if (lastKept.HasValue)
            {
                var gap = candidate.FireAt - lastKept.Value;
                if (gap < TimeSpan.FromMinutes(_settings.MinGapMinutes)) continue;
            }

            result.Add(candidate);
            lastKept = candidate.FireAt;
        }

        return result;
    }

    private bool IsInQuietHours(int hour)
    {
        var start = _settings.QuietHoursStart;
        var end   = _settings.QuietHoursEnd;

        // Overnight span (e.g. 22 → 7): quiet when hour >= start OR hour < end
        if (start > end)
            return hour >= start || hour < end;

        // Same-day span (e.g. 8 → 22): quiet when hour >= start AND hour < end
        return hour >= start && hour < end;
    }

    private static DateTimeOffset ToLocalDateTimeOffset(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time, DateTimeKind.Local);
        return new DateTimeOffset(localDateTime);
    }
}
