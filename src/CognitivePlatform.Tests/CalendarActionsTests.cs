using Moq;
using CognitivePlatform.Api.Domains.Calendar;
using CognitivePlatform.Api.Integrations.Calendar;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Tests;

public class CalendarActionsTests
{
    private readonly Mock<ICalendarProvider> _calendarMock = new();
    private readonly CalendarActions         _actions;

    public CalendarActionsTests()
    {
        _actions = new CalendarActions(_calendarMock.Object, BuildConfig());
    }

    private static IConfiguration BuildConfig(string urls = "http://localhost:5273")
        => new ConfigurationBuilder()
               .AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = urls })
               .Build();

    // ================================================================
    // GetTodayEvents
    // ================================================================

    [Fact]
    public async Task GetTodayEvents_ReturnsNotConnected_WhenCalendarNotConnected()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        var result = await _actions.GetTodayEvents();

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTodayEvents_ReturnsNoEvents_WhenCalendarIsEmpty()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>());

        var result = await _actions.GetTodayEvents();

        Assert.Contains("No events", result);
    }

    [Fact]
    public async Task GetTodayEvents_FormatsTimedEvent_WhenEventsExist()
    {
        var start = new DateTimeOffset(2026, 4, 13, 9, 0, 0, TimeSpan.Zero);
        var end   = new DateTimeOffset(2026, 4, 13, 10, 0, 0, TimeSpan.Zero);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "1"
                                             , Title    = "Team Standup"
                                             , StartUtc = start
                                             , EndUtc   = end
                                             , IsAllDay = false }
                                   });

        var result = await _actions.GetTodayEvents();

        Assert.Contains("Team Standup", result);
    }

    [Fact]
    public async Task GetTodayEvents_FormatsAllDayEvent()
    {
        var day = new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "2"
                                             , Title    = "Public Holiday"
                                             , StartUtc = day
                                             , EndUtc   = day.AddDays(1)
                                             , IsAllDay = true }
                                   });

        var result = await _actions.GetTodayEvents();

        Assert.Contains("Public Holiday", result);
        Assert.Contains("all day",         result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetTodayEvents_UsesLocalMidnightWindow_NotUtcMidnight()
    {
        DateTimeOffset capturedFrom = default;
        DateTimeOffset capturedTo   = default;

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, to, _) =>
                     {
                         capturedFrom = from;
                         capturedTo   = to;
                     })
                     .ReturnsAsync(new List<CalendarEvent>());

        await _actions.GetTodayEvents();

        // The window must span exactly one calendar day in local time.
        Assert.Equal(TimeSpan.FromHours(24), capturedTo - capturedFrom);

        // The window must be anchored at local midnight (offset == local UTC offset),
        // not at UTC midnight (offset == Zero when not in UTC timezone).
        var expectedOffset = TimeZoneInfo.Local.GetUtcOffset(capturedFrom.DateTime);
        Assert.Equal(expectedOffset, capturedFrom.Offset);
        Assert.Equal(expectedOffset, capturedTo.Offset);
    }

    // ================================================================
    // GetEventsForDate
    // ================================================================

    [Fact]
    public async Task GetEventsForDate_ReturnsNotConnected_WhenCalendarNotConnected()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        var result = await _actions.GetEventsForDate("2026-04-15");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsForDate_ReturnsParseError_WhenDateIsInvalid()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);

        var result = await _actions.GetEventsForDate("not-a-date");

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsForDate_ReturnsEvents_WhenDateIsValid()
    {
        var start = new DateTimeOffset(2026, 4, 15, 14, 0, 0, TimeSpan.Zero);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "3"
                                             , Title    = "Dentist"
                                             , StartUtc = start
                                             , EndUtc   = start.AddHours(1)
                                             , IsAllDay = false }
                                   });

        var result = await _actions.GetEventsForDate("2026-04-15");

        Assert.Contains("Dentist", result);
    }

    // ================================================================
    // AddCalendarEvent
    // ================================================================

    [Fact]
    public async Task AddCalendarEvent_ReturnsNotConnected_WhenCalendarNotConnected()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        var result = await _actions.AddCalendarEvent("Sprint Review", "2026-04-15T10:00:00");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddCalendarEvent_ReturnsParseError_WhenStartDateIsInvalid()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);

        var result = await _actions.AddCalendarEvent("Sprint Review", "bad-date");

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddCalendarEvent_DefaultsEndToOneHourAfterStart_WhenEndOmitted()
    {
        DateTimeOffset capturedStart = default;
        DateTimeOffset capturedEnd   = default;

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.AddEventAsync( It.IsAny<string>()
                                                    , It.IsAny<DateTimeOffset>()
                                                    , It.IsAny<DateTimeOffset>()
                                                    , It.IsAny<string?>()
                                                    , It.IsAny<CancellationToken>()))
                     .Callback<string, DateTimeOffset, DateTimeOffset, string?, CancellationToken>(
                             (_, startArg, endArg, _, _) =>
                             {
                                 capturedStart = startArg;
                                 capturedEnd   = endArg;
                             })
                     .ReturnsAsync(new CalendarEvent
                                   {
                                       Id       = "4"
                                     , Title    = "Sprint Review"
                                     , StartUtc = DateTimeOffset.UtcNow
                                     , EndUtc   = DateTimeOffset.UtcNow.AddHours(1)
                                   });

        await _actions.AddCalendarEvent("Sprint Review", "2026-04-15T10:00:00");

        // Verify that the end is exactly 1 hour after the start, regardless of timezone offset
        Assert.Equal(TimeSpan.FromHours(1), capturedEnd - capturedStart);
    }

    [Fact]
    public async Task AddCalendarEvent_ReturnsConfirmation_WhenEventCreated()
    {
        var start = new DateTimeOffset(2026, 4, 15, 10, 0, 0, TimeSpan.Zero);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.AddEventAsync( It.IsAny<string>()
                                                    , It.IsAny<DateTimeOffset>()
                                                    , It.IsAny<DateTimeOffset>()
                                                    , It.IsAny<string?>()
                                                    , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new CalendarEvent
                                   {
                                       Id       = "5"
                                     , Title    = "Sprint Review"
                                     , StartUtc = start
                                     , EndUtc   = start.AddHours(1)
                                   });

        var result = await _actions.AddCalendarEvent("Sprint Review", "2026-04-15T10:00:00");

        Assert.Contains("Created",       result);
        Assert.Contains("Sprint Review", result);
    }

    [Fact]
    public async Task AddCalendarEvent_ReturnsFailure_WhenProviderReturnsNull()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.AddEventAsync( It.IsAny<string>()
                                                    , It.IsAny<DateTimeOffset>()
                                                    , It.IsAny<DateTimeOffset>()
                                                    , It.IsAny<string?>()
                                                    , It.IsAny<CancellationToken>()))
                     .ReturnsAsync((CalendarEvent?)null);

        var result = await _actions.AddCalendarEvent("Sprint Review", "2026-04-15T10:00:00");

        Assert.Contains("Failed", result);
    }

    [Fact]
    public async Task AddCalendarEvent_ReturnsError_WhenEndTimeBeforeOrEqualStartTime()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);

        var result = await _actions.AddCalendarEvent("Sprint Review", "2026-04-15T10:00:00", endDateTime: "2026-04-15T09:00:00");

        Assert.Contains("end time must be after the start time", result, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================
    // FindFreeTime
    // ================================================================

    [Fact]
    public async Task FindFreeTime_ReturnsNotConnected_WhenCalendarNotConnected()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        var result = await _actions.FindFreeTime("2026-04-20", 60);

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindFreeTime_ReturnsParseError_WhenDateIsInvalid()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);

        var result = await _actions.FindFreeTime("not-a-date", 60);

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindFreeTime_ReturnsFreeSlots_WhenDayIsEmpty()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>());

        var result = await _actions.FindFreeTime("2026-04-20", 60);

        Assert.Contains("Free slots", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindFreeTime_ReturnsNoSlots_WhenDayIsFullyBooked()
    {
        // Block the entire local working-hours window (08:00–18:00 local) with a single event
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 4, 20));
        var start = new DateTimeOffset(2026, 4, 20,  8, 0, 0, localOffset);
        var end   = new DateTimeOffset(2026, 4, 20, 18, 0, 0, localOffset);

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<DateTimeOffset>()
                                                      , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarEvent>
                                   {
                                       new() { Id       = "1"
                                             , Title    = "All-day Block"
                                             , StartUtc = start
                                             , EndUtc   = end
                                             , IsAllDay = false }
                                   });

        var result = await _actions.FindFreeTime("2026-04-20", 60);

        Assert.Contains("No free slots", result, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================
    // CalendarAuthException handling (Bug 3 regression)
    // ================================================================

    [Fact]
    public async Task GetTodayEvents_ReturnsReAuthMessage_WhenTokenExpired()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new CalendarAuthException());

        var result = await _actions.GetTodayEvents();

        Assert.Contains("expired", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No events", result);
    }

    [Fact]
    public async Task GetEventsForDate_ReturnsReAuthMessage_WhenTokenExpired()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new CalendarAuthException());

        var result = await _actions.GetEventsForDate("2026-04-20");

        Assert.Contains("expired", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No events", result);
    }

    [Fact]
    public async Task FindFreeTime_ReturnsReAuthMessage_WhenTokenExpired()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new CalendarAuthException());

        var result = await _actions.FindFreeTime("2026-04-20", 60);

        Assert.Contains("expired", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No free slots", result);
    }

    // ================================================================
    // ListCalendars
    // ================================================================

    [Fact]
    public async Task ListCalendars_ReturnsNotConnected_WhenCalendarNotConnected()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        var result = await _actions.ListCalendars();

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListCalendars_ReturnsAllCalendars_WithInclusionStatus()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetCalendarListAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarSummary>
                                   {
                                       new("personal-id", "Personal",          IsSelected: true)
                                     , new("family-id",   "Family",            IsSelected: false)
                                     , new("work-id",     "Work",              IsSelected: true)
                                   });

        var result = await _actions.ListCalendars();

        Assert.Contains("Personal",  result);
        Assert.Contains("Family",    result);
        Assert.Contains("Work",      result);
        Assert.Contains("included",  result);
        Assert.Contains("excluded",  result);
    }

    // ================================================================
    // SetCalendarInclusion
    // ================================================================

    [Fact]
    public async Task SetCalendarInclusion_ReturnsNotFound_WhenNameDoesNotMatch()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetCalendarListAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarSummary>
                                   {
                                       new("personal-id", "Personal", IsSelected: true)
                                   });

        var result = await _actions.SetCalendarInclusion("Unknown Calendar", false);

        Assert.Contains("No calendar found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetCalendarInclusion_CallsProviderWithCorrectId_WhenNameMatches()
    {
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetCalendarListAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarSummary>
                                   {
                                       new("family-id", "Family", IsSelected: true)
                                   });

        _calendarMock.Setup(cal => cal.SetCalendarInclusionAsync( It.IsAny<string>()
                                                                , It.IsAny<bool>()
                                                                , It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await _actions.SetCalendarInclusion("Family", false);

        _calendarMock.Verify(cal => cal.SetCalendarInclusionAsync("family-id"
                                                                 , false
                                                                 , It.IsAny<CancellationToken>())
                           , Times.Once);

        Assert.Contains("Family",   result);
        Assert.Contains("excluded", result);
    }

    // ================================================================
    // BUG-35 — Relative date parsing
    // ================================================================

    [Fact]
    public async Task GetEventsForDate_ResolvesToday_WhenInputIsToday()
    {
        var expectedDate = DateTimeOffset.UtcNow.Date;

        DateTimeOffset capturedFrom = default;

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => capturedFrom = from)
                     .ReturnsAsync(new List<CalendarEvent>());

        await _actions.GetEventsForDate("today");

        Assert.Equal(expectedDate.Date, capturedFrom.LocalDateTime.Date);
    }

    [Fact]
    public async Task GetEventsForDate_ResolvesTomorrow_WhenInputIsTomorrow()
    {
        var expectedDate = DateTimeOffset.UtcNow.Date.AddDays(1);

        DateTimeOffset capturedFrom = default;

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(true);
        _calendarMock.Setup(cal => cal.GetEventsAsync( It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<DateTimeOffset>()
                                                     , It.IsAny<CancellationToken>()))
                     .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => capturedFrom = from)
                     .ReturnsAsync(new List<CalendarEvent>());

        await _actions.GetEventsForDate("tomorrow");

        Assert.Equal(expectedDate.Date, capturedFrom.LocalDateTime.Date);
    }

    // ================================================================
    // BUG-36 — Re-auth URL port substitution
    // ================================================================

    [Fact]
    public async Task NotConnectedMessage_ContainsActualPort_WhenCalendarNotConnected()
    {
        var actions = new CalendarActions(_calendarMock.Object
                                        , BuildConfig("http://localhost:9999"));

        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        var result = await actions.GetTodayEvents();

        Assert.DoesNotContain("<environmentPort>", result);
        Assert.Contains("9999",                    result);
    }
}
