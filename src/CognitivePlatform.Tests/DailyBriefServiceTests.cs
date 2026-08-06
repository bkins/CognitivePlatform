using Moq;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Calendar;

namespace CognitivePlatform.Tests;

public class DailyBriefServiceTests
{
    private readonly Mock<ITaskService>      _tasksMock    = new();
    private readonly Mock<ICalendarProvider> _calendarMock = new();
    private readonly DailyBriefService       _service;

    public DailyBriefServiceTests()
    {
        // Calendar not connected by default — existing tests are unaffected
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        _service = new DailyBriefService(_tasksMock.Object
                                       , _calendarMock.Object);
    }

    // ================================================================
    // DO IT NOW (Important & Urgent)
    // ================================================================

    [Fact]
    public void GetBrief_IncludesDoItNowTask_WhenImportantAndUrgent()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Fix production bug"
                                         , IsImportant      = true
                                         , IsUrgent         = true
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Fix production bug", result);
        Assert.Contains("Do It Now",          result);
    }

    [Fact]
    public void GetBrief_ShowsNoneForDoItNow_WhenNoImportantUrgentTasks()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Read a book"
                                         , IsImportant      = false
                                         , IsUrgent         = false
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Do It Now", result);
        Assert.Contains("(none)", result);
    }

    // ================================================================
    // DUE TODAY / OVERDUE
    // ================================================================

    [Fact]
    public void GetBrief_IncludesDueTask_WhenDueDateIsToday()
    {
        var today = DateTimeOffset.UtcNow.Date;
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Submit invoice"
                                         , DueDate          = new DateTimeOffset(today, TimeSpan.Zero)
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Submit invoice", result);
        Assert.Contains("Due Today",      result);
    }

    [Fact]
    public void GetBrief_IncludesOverdueLabel_WhenDueDateIsPast()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Pay bill"
                                         , DueDate          = DateTimeOffset.UtcNow.AddDays(-3)
                                   }
                           });

        var result = _service.GetBrief();

        Assert.Contains("Pay bill",  result);
        Assert.Contains("[OVERDUE]", result);
    }

    [Fact]
    public void GetBrief_DoesNotIncludeFutureTask_InDueTodaySection()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Future task"
                                         , DueDate          = DateTimeOffset.UtcNow.AddDays(5)
                                   }
                           });

        var result = _service.GetBrief();

        // Future task should not appear under Due Today, but Due Today section should show (none)
        var dueTodaySection = result.Substring(result.IndexOf("Due Today"
                                                            , StringComparison.Ordinal));
        Assert.Contains("(none)"
                      , dueTodaySection);
    }

    // ================================================================
    // STRUCTURE
    // ================================================================

    [Fact]
    public void GetBrief_AlwaysIncludesBothSections()
    {
        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>());

        var result = _service.GetBrief();

        Assert.Contains("Do It Now",            result);
        Assert.Contains("Due Today or Overdue", result);
        Assert.Contains("Daily Brief",          result);
    }

    // ================================================================
    // CALENDAR SECTION
    // ================================================================

    [Fact]
    public void GetBrief_IncludesCalendarSection_WhenProviderIsConnectedAndHasEvents()
    {
        var start = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero);
        var end   = start.AddHours(1);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "1"
                                             , Title    = "Sprint Planning"
                                             , StartUtc = start
                                             , EndUtc   = end
                                             , IsAllDay = false }
                                   });
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);
        var result  = service.GetBrief();

        Assert.Contains("Today's Calendar", result);
        Assert.Contains("Sprint Planning",  result);
    }

    [Fact]
    public void GetBrief_OmitsCalendarSection_WhenProviderIsNull()
    {
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object);
        var result  = service.GetBrief();

        Assert.DoesNotContain("Today's Calendar", result);
    }

    [Fact]
    public void GetBrief_OmitsCalendarSection_WhenProviderThrows()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new InvalidOperationException("Calendar unavailable"));
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);

        // Should not throw, and should omit the section
        var result = service.GetBrief();

        Assert.DoesNotContain("Today's Calendar", result);
    }

    // ================================================================
    // BUG-12: explicit local date honours client "today"
    // ================================================================

    [Fact]
    public void GetBrief_UsesExplicitLocalDate_InHeader()
    {
        var explicitDate = new DateOnly(2026, 1, 15);
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var result = _service.GetBrief(explicitDate);

        Assert.Contains("Daily Brief — 2026-01-15", result);
    }

    [Fact]
    public void GetBrief_FallsBackToUtcDate_InHeader_WhenLocalDateIsNull()
    {
        var utcToday = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var result = _service.GetBrief();

        Assert.Contains($"Daily Brief — {utcToday}", result);
    }

    [Fact]
    public void GetBrief_IncludesDueTask_WhenDueDateMatchesExplicitLocalDate()
    {
        // Pin the task's due date three days into the future — it would NOT appear
        // under Due Today against today's UTC date, but SHOULD appear when we pass
        // that future date as the client's local date.
        var futureDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(3));
        var futureDue  = new DateTimeOffset(futureDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        _tasksMock.Setup(svc => svc.GetActive())
                  .Returns(new List<TaskItem>
                           {
                                   new()
                                   {
                                           ShortDescription = "Submit future invoice"
                                         , DueDate          = futureDue
                                   }
                           });

        var result = _service.GetBrief(futureDate);

        Assert.Contains("Submit future invoice", result);

        var dueTodaySection = result.Substring(result.IndexOf("Due Today", StringComparison.Ordinal));
        Assert.Contains("Submit future invoice", dueTodaySection);
    }

    [Fact]
    public void GetBrief_AnchorsCalendarWindowToLocalMidnight_OfExplicitDate()
    {
        var explicitDate  = new DateOnly(2026, 4, 24);
        var expectedStart = new DateTimeOffset(explicitDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
        var expectedEnd   = expectedStart.AddDays(1);

        DateTimeOffset capturedStart = default;
        DateTimeOffset capturedEnd   = default;

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((start, end, _) =>
                      {
                          capturedStart = start;
                          capturedEnd   = end;
                      })
                     .ReturnsAsync(new List<CalendarEvent>());
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);

        service.GetBrief(explicitDate);

        Assert.Equal(expectedStart, capturedStart);
        Assert.Equal(expectedEnd,   capturedEnd);
    }

    // ================================================================
    // BUG-19: all-day event local-date filter
    // ================================================================

    [Fact]
    public void GetBrief_IncludesTodaysAllDayEvent_WhenLocalDateMatches()
    {
        var today      = DateOnly.FromDateTime(DateTime.Today);
        var startLocal = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "1"
                                             , Title    = "All Day Today"
                                             , StartUtc = startLocal
                                             , EndUtc   = startLocal.AddDays(1)
                                             , IsAllDay = true }
                                   });
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);
        var result  = service.GetBrief(today);

        Assert.Contains("All Day Today", result);
    }

    [Fact]
    public void GetBrief_ExcludesYesterdaysAllDayEvent_WhenLocalDateDoesNotMatch()
    {
        var today         = DateOnly.FromDateTime(DateTime.Today);
        var yesterdayUtc  = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1), TimeSpan.Zero);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "2"
                                             , Title    = "Yesterday Event"
                                             , StartUtc = yesterdayUtc
                                             , EndUtc   = yesterdayUtc.AddDays(1)
                                             , IsAllDay = true }
                                   });
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);
        var result  = service.GetBrief(today);

        Assert.DoesNotContain("Yesterday Event", result);
    }

    [Fact]
    public void GetBrief_IncludesTimedEvent_EvenWhenStartIsBeforeLocalMidnight()
    {
        var today          = DateOnly.FromDateTime(DateTime.Today);
        var lateNightStart = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(23), TimeSpan.Zero);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "3"
                                             , Title    = "Midnight Timed Event"
                                             , StartUtc = lateNightStart
                                             , EndUtc   = lateNightStart.AddHours(2)
                                             , IsAllDay = false }
                                   });
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);
        var result  = service.GetBrief(today);

        Assert.Contains("Midnight Timed Event", result);
    }

    [Fact]
    public void GetBrief_PassesLocalDateAnchoredWindow_ThatDiffersFromUtcMidnight_WhenServerIsNotUtc()
    {
        // Regression guard against the pre-fix calendar window which used
        // new DateTimeOffset(date, TimeSpan.Zero) — i.e. UTC midnight, regardless of
        // the server's local offset. When the server's offset is non-zero, the fixed
        // local-midnight anchor and the old UTC-midnight anchor differ; assert the
        // new code produces the local-offset anchor. On a UTC server this test is a
        // no-op (both anchors coincide), which is acceptable.
        var explicitDate  = new DateOnly(2026, 4, 24);
        var expectedStart = new DateTimeOffset(explicitDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));

        DateTimeOffset capturedStart = default;

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((start, _, _) => capturedStart = start)
                     .ReturnsAsync(new List<CalendarEvent>());
        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, _calendarMock.Object);

        service.GetBrief(explicitDate);

        Assert.Equal(expectedStart.Offset, capturedStart.Offset);
    }

    // ================================================================
    // ACTIVE INSIGHTS
    // ================================================================

    [Fact]
    public void GetBrief_IncludesActiveInsightsSection_WhenRecentInsightsExist()
    {
        var historyStoreMock = new Mock<IInsightHistoryStore>();
        historyStoreMock.Setup(store => store.GetRecentAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new List<InsightHistoryItem>
                                      {
                                          new()
                                          {
                                              Message      = "Caffeine after 2pm correlates with lower sleep quality."
                                            , Outcome      = InsightOutcome.Unknown
                                            , EmittedAtUtc = DateTime.UtcNow.AddDays(-1)
                                          }
                                      });

        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, insightHistoryStore: historyStoreMock.Object);
        var result  = service.GetBrief();

        Assert.Contains("--- Active Insights ---", result);
        Assert.Contains("• Caffeine after 2pm correlates with lower sleep quality.", result);
    }

    [Fact]
    public void GetBrief_OmitsActiveInsightsSection_WhenOnlyResolvedOrDeletedInsightsExist()
    {
        var historyStoreMock = new Mock<IInsightHistoryStore>();
        historyStoreMock.Setup(store => store.GetRecentAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new List<InsightHistoryItem>
                                      {
                                          new() { Message = "Resolved insight", Outcome = InsightOutcome.ActedOn }
                                        , new() { Message = "Deleted insight", Outcome = InsightOutcome.Unknown, IsDeleted = true }
                                      });

        _tasksMock.Setup(svc => svc.GetActive()).Returns(new List<TaskItem>());

        var service = new DailyBriefService(_tasksMock.Object, insightHistoryStore: historyStoreMock.Object);
        var result  = service.GetBrief();

        Assert.DoesNotContain("--- Active Insights ---", result);
    }
}

