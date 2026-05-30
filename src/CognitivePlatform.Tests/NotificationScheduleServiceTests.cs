using Microsoft.Extensions.Options;
using Moq;
using CognitivePlatform.Api.Services;
using CognitivePlatform.Api.Integrations.Notifications;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Tests;

public class NotificationScheduleServiceTests
{
    private readonly Mock<ITaskService>        _tasksMock   = new();
    private readonly Mock<IDailyRecordService> _recordMock  = new();
    private readonly Mock<IJournalService>     _journalMock = new();

    public NotificationScheduleServiceTests()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns((DailyRecord?)null);

        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>());
    }

    // Guard rules are disabled by default so category tests stay isolated.
    // Guard rule tests create services with explicit settings.
    private NotificationScheduleService MakeService(NotificationSettings? settings = null)
    {
        var effective = settings ?? new NotificationSettings
                                    {
                                        MaxPerDay       = 20
                                      , MinGapMinutes   = 0
                                      , QuietHoursStart = 23
                                      , QuietHoursEnd   = 23   // same-day empty span → no quiet hours
                                    };

        return new NotificationScheduleService(_tasksMock.Object
                                             , _recordMock.Object
                                             , _journalMock.Object
                                             , Options.Create(effective));
    }

    // Midnight local today — all morning/evening fire times are in the future.
    private static DateTimeOffset LocalMidnight()
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
    }

    // Arbitrary local time today — used to pass or fail the DayClose hour threshold.
    private static DateTimeOffset LocalAt(int hour, int minute = 0)
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
    }

    // ================================================================
    // DayOpen
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_EmitsDayOpen_WhenNoRecordForToday()
    {
        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.DayOpen);
    }

    [Fact]
    public async Task GetScheduleAsync_EmitsDayOpen_WhenRecordExistsButNotOpened()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = null });

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.DayOpen);
    }

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
    public async Task GetScheduleAsync_DayOpen_HasStableExternalId()
    {
        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.ToLocalTime().DateTime);

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        var dayOpen = result.Notifications.Single(notification => notification.Category == NotificationCategory.DayOpen);
        Assert.Equal($"day-open-{today:yyyy-MM-dd}", dayOpen.ExternalId);
    }

    // ================================================================
    // Journal
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_EmitsJournal_WhenNoEntryForToday()
    {
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.Journal);
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesJournal_WhenEntryExistsForToday()
    {
        var entry    = new JournalEntry    { Id = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        var revision = new JournalRevision { RevisionId = "r1", EntryId = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { new(entry, revision, false) });

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.Journal);
    }

    // ================================================================
    // DayClose
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_EmitsDayClose_WhenDayOpenedNotClosed_AndAfter8pm()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = null });

        // Suppress Journal so DayClose is the only evening candidate
        var entry    = new JournalEntry    { Id = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        var revision = new JournalRevision { RevisionId = "r1", EntryId = "e1", CreatedUtc = DateTimeOffset.UtcNow };
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { new(entry, revision, false) });

        // 8:30pm — past the 8pm threshold; DayClose fires at 9:30pm (still in future)
        var result = await MakeService().GetScheduleAsync(LocalAt(20, 30));

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.DayClose);
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesDayClose_WhenDayAlreadyClosed()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = DateTimeOffset.UtcNow
                                            , ClosedAtUtc = DateTimeOffset.UtcNow });

        var result = await MakeService().GetScheduleAsync(LocalAt(20, 30));

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.DayClose);
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesDayClose_WhenDayNotOpened()
    {
        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);

        var result = await MakeService().GetScheduleAsync(LocalAt(20, 30));

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.DayClose);
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesDayClose_WhenBefore8pm()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Returns(new DailyRecord { OpenedAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = null });

        // 3pm — before the 8pm threshold
        var result = await MakeService().GetScheduleAsync(LocalAt(15, 0));

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.DayClose);
    }

    // ================================================================
    // TaskDue — today
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_EmitsTaskDueToday_WhenActiveTaskDueToday()
    {
        var localToday = DateOnly.FromDateTime(DateTimeOffset.Now.ToLocalTime().DateTime);
        var dueDate    = new DateTimeOffset(localToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                               new() { ShortDescription = "Send invoice", DueDate = dueDate }
                           });

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.TaskDue
                                     && notification.Body.Contains("Send invoice")
                                     && notification.ExternalId.StartsWith("task-due-today-"));
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesTaskDueToday_WhenNoActiveTasksDueToday()
    {
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.ExternalId.StartsWith("task-due-today-"));
    }

    // ================================================================
    // TaskDue — tomorrow
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_EmitsTaskDueTomorrow_WhenActiveTaskDueTomorrow()
    {
        var localTomorrow = DateOnly.FromDateTime(DateTimeOffset.Now.ToLocalTime().DateTime).AddDays(1);
        var dueDate       = new DateTimeOffset(localTomorrow.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                               new() { ShortDescription = "Team standup", DueDate = dueDate }
                           });

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.TaskDue
                                     && notification.Body.Contains("Team standup")
                                     && notification.ExternalId.StartsWith("task-due-tomorrow-"));
    }

    [Fact]
    public async Task GetScheduleAsync_SuppressesTaskDueTomorrow_WhenNoActiveTasksDueTomorrow()
    {
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.ExternalId.StartsWith("task-due-tomorrow-"));
    }

    // ================================================================
    // Guard — MaxPerDay
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_LimitsOutput_WhenMaxPerDayExceeded()
    {
        // DayOpen + Journal both emittable — but MaxPerDay = 1
        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

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
    // Guard — MinGap
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_DropsCloselySpacedNotification_WhenMinGapNotMet()
    {
        // DayOpen fires at 8:00am; TaskDueToday fires at 8:30am (30-min gap).
        // MinGapMinutes = 120 → the second notification should be dropped.
        var localToday = DateOnly.FromDateTime(DateTimeOffset.Now.ToLocalTime().DateTime);
        var dueDate    = new DateTimeOffset(localToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                               new() { ShortDescription = "Standup", DueDate = dueDate }
                           });

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
                            , notification => notification.ExternalId.StartsWith("task-due-today-"));
    }

    // ================================================================
    // Guard — QuietHours
    // ================================================================

    [Fact]
    public async Task GetScheduleAsync_DropsNotification_WhenFireTimeIsInQuietHours()
    {
        // Quiet hours 8am–9am: DayOpen fires at 8:00am → suppressed.
        // Journal fires at 9pm → outside the quiet window → kept.
        _recordMock.Setup(svc => svc.GetToday()).Returns((DailyRecord?)null);
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var service = MakeService(new NotificationSettings
                                  {
                                      MaxPerDay       = 20
                                    , MinGapMinutes   = 0
                                    , QuietHoursStart = 8
                                    , QuietHoursEnd   = 9   // 8am–9am quiet
                                  });

        var result = await service.GetScheduleAsync(LocalMidnight());

        Assert.DoesNotContain(result.Notifications
                            , notification => notification.Category == NotificationCategory.DayOpen);

        Assert.Contains(result.Notifications
                      , notification => notification.Category == NotificationCategory.Journal);
    }

    [Fact]
    public async Task GetScheduleAsync_ReturnsEmptySchedule_WhenServiceThrows()
    {
        _recordMock.Setup(svc => svc.GetToday())
                   .Throws(new InvalidOperationException("Storage unavailable"));

        var result = await MakeService().GetScheduleAsync(LocalMidnight());

        Assert.Empty(result.Notifications);
    }
}
