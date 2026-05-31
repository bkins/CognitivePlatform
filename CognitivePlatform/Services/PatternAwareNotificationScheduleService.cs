using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Notifications;
using CognitivePlatform.Api.Wellbeing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Services;

/// <summary>
/// Pattern-aware replacement for <see cref="NotificationScheduleService"/> (Phase N.2).
/// Uses learned fire times from <see cref="INotificationPatternService"/> where history
/// is sufficient, and falls back to the hardcoded defaults otherwise.
/// </summary>
public sealed class PatternAwareNotificationScheduleService : INotificationScheduleProvider
{
    private static readonly TimeOnly DefaultDayOpenTime   = new(8,  0);
    private static readonly TimeOnly DefaultDayCloseTime  = new(21, 30);
    private static readonly TimeOnly DefaultJournalTime   = new(21, 0);
    private static readonly TimeOnly DefaultWellbeingTime = new(14, 0);

    private readonly ITaskService                                      _taskService;
    private readonly IDailyRecordService                               _dailyRecordService;
    private readonly IJournalService                                   _journalService;
    private readonly IWellbeingPatternService                          _wellbeingPatternService;
    private readonly INotificationPatternService                       _patternService;
    private readonly NotificationSettings                              _settings;
    private readonly ILogger<PatternAwareNotificationScheduleService>  _logger;

    public PatternAwareNotificationScheduleService(
        ITaskService                                       taskService
      , IDailyRecordService                                dailyRecordService
      , IJournalService                                    journalService
      , IWellbeingPatternService                           wellbeingPatternService
      , INotificationPatternService                        patternService
      , IOptions<NotificationSettings>                     settings
      , ILogger<PatternAwareNotificationScheduleService>?  logger = null )
    {
        _taskService             = taskService             ?? throw new ArgumentNullException(nameof(taskService));
        _dailyRecordService      = dailyRecordService      ?? throw new ArgumentNullException(nameof(dailyRecordService));
        _journalService          = journalService          ?? throw new ArgumentNullException(nameof(journalService));
        _wellbeingPatternService = wellbeingPatternService ?? throw new ArgumentNullException(nameof(wellbeingPatternService));
        _patternService          = patternService          ?? throw new ArgumentNullException(nameof(patternService));
        _settings                = settings?.Value         ?? new NotificationSettings();
        _logger                  = logger                  ?? NullLogger<PatternAwareNotificationScheduleService>.Instance;
    }

    public async Task<NotificationSchedule> GetScheduleAsync(DateTimeOffset from, CancellationToken ct = default)
    {
        try
        {
            var localFrom = from.ToLocalTime();
            var today     = DateOnly.FromDateTime(localFrom.DateTime);
            var tomorrow  = today.AddDays(1);

            var dayOpenTime  = await _patternService.LearnedOpenDayTimeAsync(ct)  ?? DefaultDayOpenTime;
            var dayCloseTime = await _patternService.LearnedCloseDayTimeAsync(ct) ?? DefaultDayCloseTime;
            var journalTime  = await _patternService.LearnedJournalTimeAsync(ct)  ?? DefaultJournalTime;

            var candidates = new List<ScheduledNotification>();

            BuildDayOpenCandidate(from, today, dayOpenTime, candidates);
            BuildDayCloseCandidate(from, today, localFrom, dayCloseTime, candidates);
            BuildJournalCandidate(from, today, journalTime, candidates);
            BuildTaskDueCandidates(from, today, tomorrow, candidates);
            await BuildWellbeingCheckInCandidateAsync(from, today, candidates, ct);

            var ordered  = candidates.OrderBy(notification => notification.FireAt).ToList();
            var filtered = ApplyGuardRules(ordered);

            return new NotificationSchedule { Notifications = filtered };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PatternAwareNotificationScheduleService failed to build schedule");
            return new NotificationSchedule();
        }
    }

    private void BuildDayOpenCandidate( DateTimeOffset              from
                                      , DateOnly                    today
                                      , TimeOnly                    fireTime
                                      , List<ScheduledNotification> candidates )
    {
        var todayRecord = _dailyRecordService.GetToday();
        if (todayRecord is not null && todayRecord.OpenedAtUtc is not null)
            return;

        var fireAt = ToLocalDateTimeOffset(today, fireTime);
        if (fireAt <= from) return;

        candidates.Add(new ScheduledNotification
                        {
                            ExternalId = $"day-open-{today:yyyy-MM-dd}"
                          , Title      = "Open Your Day"
                          , Body       = "You haven't opened your day yet. Ready to set your intentions?"
                          , FireAt     = fireAt
                          , Category   = NotificationCategory.DayOpen
                        });
    }

    private void BuildDayCloseCandidate( DateTimeOffset              from
                                       , DateOnly                    today
                                       , DateTimeOffset              localFrom
                                       , TimeOnly                    fireTime
                                       , List<ScheduledNotification> candidates )
    {
        if (localFrom.Hour < 20) return;

        var todayRecord = _dailyRecordService.GetToday();
        if (todayRecord is null || todayRecord.OpenedAtUtc is null || todayRecord.ClosedAtUtc is not null)
            return;

        var fireAt = ToLocalDateTimeOffset(today, fireTime);
        if (fireAt <= from) return;

        candidates.Add(new ScheduledNotification
                        {
                            ExternalId = $"day-close-{today:yyyy-MM-dd}"
                          , Title      = "Close Your Day"
                          , Body       = "You opened your day but haven't closed it yet. Take a moment to reflect."
                          , FireAt     = fireAt
                          , Category   = NotificationCategory.DayClose
                        });
    }

    private void BuildJournalCandidate( DateTimeOffset              from
                                      , DateOnly                    today
                                      , TimeOnly                    fireTime
                                      , List<ScheduledNotification> candidates )
    {
        var dayStartUtc = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local)).ToUniversalTime();
        var dayEndUtc   = dayStartUtc.AddDays(1);
        var entries     = _journalService.ListEntries(dayStartUtc, dayEndUtc);
        if (entries.Count > 0) return;

        var fireAt = ToLocalDateTimeOffset(today, fireTime);
        if (fireAt <= from) return;

        candidates.Add(new ScheduledNotification
                        {
                            ExternalId = $"journal-{today:yyyy-MM-dd}"
                          , Title      = "Journal Check-In"
                          , Body       = "You haven't journaled today. A moment of reflection goes a long way."
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

    private async Task BuildWellbeingCheckInCandidateAsync(
        DateTimeOffset              from
      , DateOnly                    today
      , List<ScheduledNotification> candidates
      , CancellationToken           ct )
    {
        var fireAt = ToLocalDateTimeOffset(today, DefaultWellbeingTime);
        if (fireAt <= from) return;

        try
        {
            var report  = await _wellbeingPatternService.AnalyseAsync(today, today, ct);
            var pattern = report.Patterns
                .Where(wellbeingPattern => wellbeingPattern.Severity is PatternSeverity.Concern or PatternSeverity.Attention)
                .OrderByDescending(wellbeingPattern => (int)wellbeingPattern.Severity)
                .FirstOrDefault();

            if (pattern is null) return;

            candidates.Add(new ScheduledNotification
                            {
                                ExternalId = $"wellbeing-checkin-{today:yyyy-MM-dd}"
                              , Title      = "Wellbeing check-in"
                              , Body       = pattern.Description
                              , FireAt     = fireAt
                              , Category   = NotificationCategory.CheckIn
                            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Wellbeing check-in rule skipped due to analysis failure");
        }
    }

    private IReadOnlyList<ScheduledNotification> ApplyGuardRules(List<ScheduledNotification> candidates)
    {
        var result  = new List<ScheduledNotification>();
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
