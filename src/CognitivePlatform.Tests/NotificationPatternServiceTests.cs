using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Integrations.Notifications;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Services;
using Moq;

namespace CognitivePlatform.Tests;

public class NotificationPatternServiceTests
{
    private readonly Mock<IDailyRecordService> _recordMock  = new();
    private readonly Mock<IJournalService>     _journalMock = new();
    private readonly Mock<IObjectStore>        _storeMock   = new();

    public NotificationPatternServiceTests()
    {
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(new List<DailyRecord>());

        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        _storeMock.Setup(store => store.Save(It.IsAny<NotificationFeedbackRecord>(), It.IsAny<string?>(), It.IsAny<string?>()))
                  .ReturnsAsync("saved-id");
    }

    private NotificationPatternService MakeService()
        => new(_recordMock.Object, _journalMock.Object, _storeMock.Object);

    // Creates a DailyRecord that was opened at the given local hour:minute today.
    private static DailyRecord RecordWithOpenAt(int hour, int minute)
        => new() { OpenedAtUtc = LocalToday(hour, minute) };

    // Creates a DailyRecord opened at 8 AM and closed at the given local hour:minute today.
    private static DailyRecord RecordWithCloseAt(int hour, int minute)
        => new() { OpenedAtUtc = LocalToday(8, 0), ClosedAtUtc = LocalToday(hour, minute) };

    // Returns a DateTimeOffset representing local-time today at hour:minute.
    private static DateTimeOffset LocalToday(int hour, int minute)
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
    }

    // Creates a JournalEntryWithRevision whose CreatedUtc equals the local hour:minute converted to UTC.
    private static JournalEntryWithRevision EntryAt(int hour, int minute)
    {
        var local = LocalToday(hour, minute);
        var entry = new JournalEntry
                    {
                        Id         = Guid.NewGuid().ToString("N")
                      , CreatedUtc = local.ToUniversalTime()
                    };
        var revision = new JournalRevision
                       {
                           RevisionId = "r1"
                         , EntryId    = entry.Id
                         , CreatedUtc = entry.CreatedUtc
                       };
        return new JournalEntryWithRevision(entry, revision, false);
    }

    // ================================================================
    // LearnedOpenDayTimeAsync
    // ================================================================

    [Fact]
    public async Task LearnedOpenDayTimeAsync_ReturnsAverage_WhenEnoughDataPoints()
    {
        var records = Enumerable.Repeat(RecordWithOpenAt(8, 0), 7).ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedOpenDayTimeAsync();

        Assert.NotNull(result);
        Assert.Equal(new TimeOnly(8, 0), result.Value);
    }

    [Fact]
    public async Task LearnedOpenDayTimeAsync_ReturnsNull_WhenFewerThanSevenDataPoints()
    {
        var records = Enumerable.Repeat(RecordWithOpenAt(8, 0), 6).ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedOpenDayTimeAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LearnedOpenDayTimeAsync_ReturnsNull_WhenNoRecords()
    {
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(new List<DailyRecord>());

        var result = await MakeService().LearnedOpenDayTimeAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LearnedOpenDayTimeAsync_ExcludesSoftDeletedRecords()
    {
        // 6 valid + 1 deleted = 7 total, but only 6 non-deleted → below minimum
        var records = Enumerable.Repeat(RecordWithOpenAt(8, 0), 6)
                                .Append(new DailyRecord { OpenedAtUtc = LocalToday(8, 0), IsDeleted = true })
                                .ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedOpenDayTimeAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LearnedOpenDayTimeAsync_ReturnsCorrectAverage_WhenTimesVary()
    {
        // 3 records at 8:00 (480 min) + 4 records at 9:00 (540 min)
        // avg = (3×480 + 4×540) / 7 = 3720 / 7 ≈ 531.4 → rounds to 531 → 8:51
        var records = Enumerable.Repeat(RecordWithOpenAt(8, 0), 3)
                                .Concat(Enumerable.Repeat(RecordWithOpenAt(9, 0), 4))
                                .ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedOpenDayTimeAsync();

        Assert.NotNull(result);
        var expectedMinutes = (int)Math.Round((3.0 * 480 + 4.0 * 540) / 7.0);
        Assert.Equal(new TimeOnly(expectedMinutes / 60, expectedMinutes % 60), result.Value);
    }

    [Fact]
    public async Task LearnedOpenDayTimeAsync_QueriesLast14Days()
    {
        DateOnly? capturedFrom = null;
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Callback<DateOnly, DateOnly>((from, _) => capturedFrom = from)
                   .Returns(new List<DailyRecord>());

        await MakeService().LearnedOpenDayTimeAsync();

        var today    = DateOnly.FromDateTime(DateTime.Today);
        var expected = today.AddDays(-14);
        Assert.Equal(expected, capturedFrom);
    }

    // ================================================================
    // LearnedCloseDayTimeAsync
    // ================================================================

    [Fact]
    public async Task LearnedCloseDayTimeAsync_ReturnsAverage_WhenEnoughDataPoints()
    {
        var records = Enumerable.Repeat(RecordWithCloseAt(21, 30), 7).ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedCloseDayTimeAsync();

        Assert.NotNull(result);
        Assert.Equal(new TimeOnly(21, 30), result.Value);
    }

    [Fact]
    public async Task LearnedCloseDayTimeAsync_ReturnsNull_WhenFewerThanSevenDataPoints()
    {
        var records = Enumerable.Repeat(RecordWithCloseAt(21, 30), 4).ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedCloseDayTimeAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LearnedCloseDayTimeAsync_ExcludesRecordsWithoutCloseTime()
    {
        // 6 closed + 1 open-only → 6 data points → below minimum
        var records = Enumerable.Repeat(RecordWithCloseAt(21, 30), 6)
                                .Append(new DailyRecord { OpenedAtUtc = LocalToday(8, 0) })
                                .ToList();
        _recordMock.Setup(svc => svc.GetRange(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
                   .Returns(records);

        var result = await MakeService().LearnedCloseDayTimeAsync();

        Assert.Null(result);
    }

    // ================================================================
    // LearnedJournalTimeAsync
    // ================================================================

    [Fact]
    public async Task LearnedJournalTimeAsync_ReturnsAverage_WhenEnoughEntries()
    {
        var entries = Enumerable.Repeat(EntryAt(21, 0), 7).ToList();
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(entries);

        var result = await MakeService().LearnedJournalTimeAsync();

        Assert.NotNull(result);
        Assert.Equal(new TimeOnly(21, 0), result.Value);
    }

    [Fact]
    public async Task LearnedJournalTimeAsync_ReturnsNull_WhenFewerThanSevenEntries()
    {
        var entries = Enumerable.Repeat(EntryAt(21, 0), 3).ToList();
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(entries);

        var result = await MakeService().LearnedJournalTimeAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LearnedJournalTimeAsync_ReturnsNull_WhenNoEntries()
    {
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = await MakeService().LearnedJournalTimeAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LearnedJournalTimeAsync_ReturnsCorrectAverage_WhenTimesVary()
    {
        // 4 entries at 20:00 (1200 min) + 3 entries at 22:00 (1320 min)
        // avg = (4×1200 + 3×1320) / 7 = 8760 / 7 ≈ 1251.4 → 1251 → 20:51
        var entries = Enumerable.Repeat(EntryAt(20, 0), 4)
                                .Concat(Enumerable.Repeat(EntryAt(22, 0), 3))
                                .ToList();
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(entries);

        var result = await MakeService().LearnedJournalTimeAsync();

        Assert.NotNull(result);
        var expectedMinutes = (int)Math.Round((4.0 * 1200 + 3.0 * 1320) / 7.0);
        Assert.Equal(new TimeOnly(expectedMinutes / 60, expectedMinutes % 60), result.Value);
    }

    // ================================================================
    // RecordFeedbackAsync
    // ================================================================

    [Fact]
    public async Task RecordFeedbackAsync_SavesWithCorrectKey()
    {
        string? capturedId = null;
        _storeMock.Setup(store => store.Save(It.IsAny<NotificationFeedbackRecord>(), It.IsAny<string?>(), It.IsAny<string?>()))
                  .Callback<NotificationFeedbackRecord, string?, string?>((_, _, id) => capturedId = id)
                  .ReturnsAsync("saved-id");

        await MakeService().RecordFeedbackAsync("day-open-2026-05-30", NotificationFeedback.Tapped);

        Assert.Equal("notification.feedback.day-open-2026-05-30", capturedId);
    }

    [Fact]
    public async Task RecordFeedbackAsync_PersistsCorrectExternalIdAndFeedbackValue()
    {
        NotificationFeedbackRecord? saved = null;
        _storeMock.Setup(store => store.Save(It.IsAny<NotificationFeedbackRecord>(), It.IsAny<string?>(), It.IsAny<string?>()))
                  .Callback<NotificationFeedbackRecord, string?, string?>((record, _, _) => saved = record)
                  .ReturnsAsync("saved-id");

        await MakeService().RecordFeedbackAsync("day-open-2026-05-30", NotificationFeedback.Dismissed);

        Assert.NotNull(saved);
        Assert.Equal("day-open-2026-05-30",        saved!.ExternalId);
        Assert.Equal(NotificationFeedback.Dismissed, saved.Feedback);
    }

    [Fact]
    public async Task RecordFeedbackAsync_SetsRecordedAtToApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow;
        NotificationFeedbackRecord? saved = null;
        _storeMock.Setup(store => store.Save(It.IsAny<NotificationFeedbackRecord>(), It.IsAny<string?>(), It.IsAny<string?>()))
                  .Callback<NotificationFeedbackRecord, string?, string?>((record, _, _) => saved = record)
                  .ReturnsAsync("saved-id");

        await MakeService().RecordFeedbackAsync("wellbeing-checkin-2026-05-30", NotificationFeedback.ActedUpon);

        var after = DateTimeOffset.UtcNow;
        Assert.NotNull(saved);
        Assert.InRange(saved!.RecordedAt, before, after);
    }
}
