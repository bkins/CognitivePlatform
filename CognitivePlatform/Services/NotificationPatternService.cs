using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Integrations.Notifications;

namespace CognitivePlatform.Api.Services;

public sealed class NotificationPatternService : INotificationPatternService
{
    private const int LookbackDays  = 14;
    private const int MinDataPoints = 7;

    private readonly IDailyRecordService _dailyRecordService;
    private readonly IJournalService     _journalService;
    private readonly IObjectStore        _store;

    public NotificationPatternService( IDailyRecordService dailyRecordService
                                     , IJournalService     journalService
                                     , IObjectStore        store )
    {
        _dailyRecordService = dailyRecordService ?? throw new ArgumentNullException(nameof(dailyRecordService));
        _journalService     = journalService     ?? throw new ArgumentNullException(nameof(journalService));
        _store              = store              ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<TimeOnly?> LearnedOpenDayTimeAsync(CancellationToken ct = default)
    {
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var records = _dailyRecordService.GetRange(today.AddDays(-LookbackDays), today);

        var minutesFromMidnight = records
            .Where(record => record.OpenedAtUtc.HasValue && !record.IsDeleted)
            .Select(record => record.OpenedAtUtc!.Value.ToLocalTime())
            .Select(local  => local.Hour * 60 + local.Minute)
            .ToList();

        if (minutesFromMidnight.Count < MinDataPoints)
            return Task.FromResult<TimeOnly?>(null);

        var avg = (int)Math.Round(minutesFromMidnight.Average());
        return Task.FromResult<TimeOnly?>(new TimeOnly(avg / 60, avg % 60));
    }

    public Task<TimeOnly?> LearnedCloseDayTimeAsync(CancellationToken ct = default)
    {
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var records = _dailyRecordService.GetRange(today.AddDays(-LookbackDays), today);

        var minutesFromMidnight = records
            .Where(record => record.ClosedAtUtc.HasValue && !record.IsDeleted)
            .Select(record => record.ClosedAtUtc!.Value.ToLocalTime())
            .Select(local  => local.Hour * 60 + local.Minute)
            .ToList();

        if (minutesFromMidnight.Count < MinDataPoints)
            return Task.FromResult<TimeOnly?>(null);

        var avg = (int)Math.Round(minutesFromMidnight.Average());
        return Task.FromResult<TimeOnly?>(new TimeOnly(avg / 60, avg % 60));
    }

    public Task<TimeOnly?> LearnedJournalTimeAsync(CancellationToken ct = default)
    {
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var fromUtc = new DateTimeOffset(today.AddDays(-LookbackDays).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                          .ToUniversalTime();
        var toUtc   = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local))
                          .ToUniversalTime();

        var entries = _journalService.ListEntries(fromUtc, toUtc);

        var minutesFromMidnight = entries
            .Select(entryWithRevision => entryWithRevision.Entry.CreatedUtc.ToLocalTime())
            .Select(local            => local.Hour * 60 + local.Minute)
            .ToList();

        if (minutesFromMidnight.Count < MinDataPoints)
            return Task.FromResult<TimeOnly?>(null);

        var avg = (int)Math.Round(minutesFromMidnight.Average());
        return Task.FromResult<TimeOnly?>(new TimeOnly(avg / 60, avg % 60));
    }

    public async Task RecordFeedbackAsync(string externalId, NotificationFeedback feedback, CancellationToken ct = default)
    {
        var record = new NotificationFeedbackRecord
                     {
                         ExternalId = externalId
                       , Feedback   = feedback
                       , RecordedAt = DateTimeOffset.UtcNow
                     };

        await _store.Save(record, id: $"notification.feedback.{externalId}");
    }
}
