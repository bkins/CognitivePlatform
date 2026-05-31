using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Notifications;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Services;
using CognitivePlatform.Api.Wellbeing;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class PatternAwareNotificationScheduleServiceTests
{
    private readonly Mock<ITaskService>               _tasksMock    = new();
    private readonly Mock<IDailyRecordService>        _recordMock   = new();
    private readonly Mock<IJournalService>            _journalMock  = new();
    private readonly Mock<IWellbeingPatternService>   _wellbeingMock = new();
    private readonly Mock<INotificationPatternService> _patternMock  = new();

    public PatternAwareNotificationScheduleServiceTests()
    {
        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);

        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        _wellbeingMock.Setup(svc => svc.AnalyseAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new WellbeingReport { Patterns = [] });

        // Default: no learned times — all fall back to hardcoded defaults
        _patternMock.Setup(svc => svc.LearnedOpenDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync((TimeOnly?)null);
        _patternMock.Setup(svc => svc.LearnedCloseDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync((TimeOnly?)null);
        _patternMock.Setup(svc => svc.LearnedJournalTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync((TimeOnly?)null);
    }

    // Guard rules are disabled by default so category-specific tests stay isolated.
    private PatternAwareNotificationScheduleService MakeService(NotificationSettings? settings = null)
    {
        var effective = settings ?? new NotificationSettings
                                    {
                                        MaxPerDay       = 20
                                      , MinGapMinutes   = 0
                                      , QuietHoursStart = 23
                                      , QuietHoursEnd   = 23  // same-day empty span → no quiet hours
                                    };

        return new PatternAwareNotificationScheduleService(
            _tasksMock.Object
          , _recordMock.Object
          , _journalMock.Object
          , _wellbeingMock.Object
          , _patternMock.Object
          , Options.Create(effective));
    }

    private static DateTimeOffset LocalMidnight()
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
    }

    private static DateTimeOffset LocalAt(int hour, int minute = 0)
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
    }

    // ================================================================
    // Learned fire times — DayOpen
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_UsesLearnedDayOpenTime_WhenPatternServiceReturnsTime()
    {
        _patternMock.Setup(svc => svc.LearnedOpenDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TimeOnly(7, 15));

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        var dayOpen = result.Notifications.Single(notification => notification.Category == NotificationCategory.DayOpen);
        var local   = dayOpen.FireAt.ToLocalTime();
        Assert.Equal(7,  local.Hour);
        Assert.Equal(15, local.Minute);
    }

    [Fact]
    public async Task GetScheduleAsync_FallsBackToDefaultDayOpenTime_WhenLearnedTimeIsNull()
    {
        _patternMock.Setup(svc => svc.LearnedOpenDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync((TimeOnly?)null);

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        var dayOpen = result.Notifications.Single(notification => notification.Category == NotificationCategory.DayOpen);
        var local   = dayOpen.FireAt.ToLocalTime();
        Assert.Equal(8, local.Hour);
        Assert.Equal(0, local.Minute);
    }

    // ================================================================
    // Learned fire times — Journal
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_UsesLearnedJournalTime_WhenPatternServiceReturnsTime()
    {
        _patternMock.Setup(svc => svc.LearnedJournalTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TimeOnly(20, 30));

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        var journal = result.Notifications.Single(notification => notification.Category == NotificationCategory.Journal);
        var local   = journal.FireAt.ToLocalTime();
        Assert.Equal(20, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    [Fact]
    public async Task GetScheduleAsync_FallsBackToDefaultJournalTime_WhenLearnedTimeIsNull()
    {
        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        var journal = result.Notifications.Single(notification => notification.Category == NotificationCategory.Journal);
        var local   = journal.FireAt.ToLocalTime();
        Assert.Equal(21, local.Hour);
        Assert.Equal(0,  local.Minute);
    }

    // ================================================================
    // Learned fire times — DayClose
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_UsesLearnedDayCloseTime_WhenPatternServiceReturnsTime()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = null });
        _patternMock.Setup(svc => svc.LearnedCloseDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TimeOnly(22, 0));

        // Suppress Journal so DayClose can appear without interference
        var entry    = new JournalEntry    { Id = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        var revision = new JournalRevision { RevisionId = "r1", EntryId = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { new(entry, revision, false) });

        var result = await MakeService().GetScheduleAsync(LocalAt(20, 30));

        var dayClose = result.Notifications.Single(notification => notification.Category == NotificationCategory.DayClose);
        var local    = dayClose.FireAt.ToLocalTime();
        Assert.Equal(22, local.Hour);
        Assert.Equal(0,  local.Minute);
    }

    [Fact]
    public async Task GetScheduleAsync_FallsBackToDefaultDayCloseTime_WhenLearnedTimeIsNull()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = null });

        var entry    = new JournalEntry    { Id = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        var revision = new JournalRevision { RevisionId = "r1", EntryId = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { new(entry, revision, false) });

        var result = await MakeService().GetScheduleAsync(LocalAt(20, 30));

        var dayClose = result.Notifications.Single(notification => notification.Category == NotificationCategory.DayClose);
        var local    = dayClose.FireAt.ToLocalTime();
        Assert.Equal(21, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    // ================================================================
    // Condition checks remain intact
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_SuppressesDayOpen_WhenDayAlreadyOpened()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = DateTimeOffset.UtcNow });

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.DayOpen);
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesLearnedDayOpen_WhenFireTimeIsInThePast()
    {
        // Learned time is 6:00 AM, from is 7:00 AM — notification must be suppressed
        _patternMock.Setup(svc => svc.LearnedOpenDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TimeOnly(6, 0));

        var result = await MakeService().GetScheduleAsync(LocalAt(7, 0));

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.DayOpen);
    }

    // ================================================================
    // Guard rules still apply with learned times
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_StillAppliesMinGap_WhenLearnedTimesAreClose()
    {
        // DayOpen learned at 8:00, Journal learned at 8:05 → 5-min gap < MinGapMinutes=120 → Journal dropped
        _patternMock.Setup(svc => svc.LearnedOpenDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TimeOnly(8, 0));
        _patternMock.Setup(svc => svc.LearnedJournalTimeAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TimeOnly(8, 5));

        var service = MakeService(new NotificationSettings
                                  {
                                      MaxPerDay       = 20
                                    , MinGapMinutes   = 120
                                    , QuietHoursStart = 23
                                    , QuietHoursEnd   = 23
                                  });

        var result = await service.GetScheduleAsync(LocalMidnight());

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.DayOpen);
        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.Journal);
    }

    [Fact]
    public async Task GetScheduleAsync_StillAppliesMaxPerDay()
    {
        var service = MakeService(new NotificationSettings
                                  {
                                      MaxPerDay       = 1
                                    , MinGapMinutes   = 0
                                    , QuietHoursStart = 23
                                    , QuietHoursEnd   = 23
                                  });

        var result = await service.GetScheduleAsync(LocalMidnight());

        Assert.True(result.Notifications.Count <= 1);
    }

    // ================================================================
    // Error resilience
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_ReturnsEmptySchedule_WhenPatternServiceThrows()
    {
        _patternMock.Setup(svc => svc.LearnedOpenDayTimeAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Pattern service unavailable"));

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Empty(result.Notifications);
    }

    [Fact]
    public async Task GetScheduleAsync_ReturnsEmptySchedule_WhenUnderlyingServiceThrows()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Throws(new InvalidOperationException("Storage unavailable"));

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Empty(result.Notifications);
    }
}
