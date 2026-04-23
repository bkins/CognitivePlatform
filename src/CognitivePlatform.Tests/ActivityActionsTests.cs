using Moq;
using CognitivePlatform.Api.Domains.Activity;

namespace CognitivePlatform.Tests;

public class ActivityActionsTests
{
    private readonly Mock<IActivityLog> _logMock = new();
    private readonly ActivityActions    _actions;
    private readonly List<ActivityEvent> _captured = new();

    public ActivityActionsTests()
    {
        _logMock.Setup(log => log.LogAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
                .Callback<ActivityEvent, CancellationToken>((activityEvent, _) => _captured.Add(activityEvent))
                .Returns(Task.CompletedTask);

        _actions = new ActivityActions(_logMock.Object);
    }

    // ================================================================
    // LOG: parameter combinations
    // ================================================================

    [Fact]
    public async Task LogActivity_Persists_WhenOnlyTypeSupplied()
    {
        var result = await _actions.LogActivity("run");

        var logged = Assert.Single(_captured);
        Assert.Equal("run", logged.ActivityType);
        Assert.Null(logged.Duration);
        Assert.Null(logged.Unit);
        Assert.Null(logged.Notes);
        Assert.Empty(logged.Tags);
        Assert.Contains("Logged run", result);
    }

    [Fact]
    public async Task LogActivity_NormalisesType_ToLowercaseTrimmed()
    {
        await _actions.LogActivity("  Running  ");

        var logged = Assert.Single(_captured);
        Assert.Equal("running", logged.ActivityType);
    }

    [Fact]
    public async Task LogActivity_ReturnsError_WhenTypeIsWhitespace()
    {
        var result = await _actions.LogActivity("   ");

        Assert.Empty(_captured);
        Assert.Contains("type is required", result);
    }

    [Fact]
    public async Task LogActivity_PersistsDurationAndUnit_WhenSupplied()
    {
        var result = await _actions.LogActivity("run", duration: 30, unit: "minutes");

        var logged = Assert.Single(_captured);
        Assert.Equal(30,        logged.Duration);
        Assert.Equal("minutes", logged.Unit);
        Assert.Contains("30",       result);
        Assert.Contains("minutes",  result);
    }

    [Fact]
    public async Task LogActivity_AllowsDurationWithoutUnit()
    {
        await _actions.LogActivity("pushups", duration: 20);

        var logged = Assert.Single(_captured);
        Assert.Equal(20, logged.Duration);
        Assert.Null(logged.Unit);
    }

    [Fact]
    public async Task LogActivity_AllowsUnitWithoutDuration()
    {
        await _actions.LogActivity("reading", unit: "pages");

        var logged = Assert.Single(_captured);
        Assert.Null(logged.Duration);
        Assert.Equal("pages", logged.Unit);
    }

    [Fact]
    public async Task LogActivity_PersistsNotes_WhenSupplied()
    {
        await _actions.LogActivity("run", notes: "felt strong");

        var logged = Assert.Single(_captured);
        Assert.Equal("felt strong", logged.Notes);
    }

    [Fact]
    public async Task LogActivity_PersistsAllFields_WhenAllSupplied()
    {
        await _actions.LogActivity("run"
                                 , duration: 30
                                 , unit: "minutes"
                                 , notes: "felt strong"
                                 , tags: "cardio, outdoors");

        var logged = Assert.Single(_captured);
        Assert.Equal("run",         logged.ActivityType);
        Assert.Equal(30,            logged.Duration);
        Assert.Equal("minutes",     logged.Unit);
        Assert.Equal("felt strong", logged.Notes);
        Assert.Equal(new[] { "cardio", "outdoors" }, logged.Tags);
    }

    // ================================================================
    // LOG: tag parsing
    // ================================================================

    [Fact]
    public async Task LogActivity_SplitsTags_OnComma()
    {
        await _actions.LogActivity("run", tags: "cardio,outdoors,morning");

        var logged = Assert.Single(_captured);
        Assert.Equal(new[] { "cardio", "outdoors", "morning" }, logged.Tags);
    }

    [Fact]
    public async Task LogActivity_TrimsWhitespaceAroundTags()
    {
        await _actions.LogActivity("run", tags: "  cardio ,   outdoors ,  morning  ");

        var logged = Assert.Single(_captured);
        Assert.Equal(new[] { "cardio", "outdoors", "morning" }, logged.Tags);
    }

    [Fact]
    public async Task LogActivity_DropsEmptyTokens_FromTags()
    {
        await _actions.LogActivity("run", tags: "cardio,,outdoors,   ,morning");

        var logged = Assert.Single(_captured);
        Assert.Equal(new[] { "cardio", "outdoors", "morning" }, logged.Tags);
    }

    [Fact]
    public async Task LogActivity_ProducesEmptyTagList_WhenTagsBlank()
    {
        await _actions.LogActivity("run", tags: "   ");

        var logged = Assert.Single(_captured);
        Assert.Empty(logged.Tags);
    }

    // ================================================================
    // LIST: formatting for 0 / 1 / many
    // ================================================================

    [Fact]
    public async Task ListActivities_ReturnsEmptyMessage_WhenNoEvents()
    {
        _logMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>());

        var result = await _actions.ListActivities();

        Assert.Contains("No activities logged", result);
    }

    [Fact]
    public async Task ListActivities_FormatsSingleEvent()
    {
        var events = new List<ActivityEvent>
                     {
                             new() { ActivityType = "run", Duration = 30, Unit = "minutes", OccurredUtc = new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero) }
                     };

        _logMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ActivityEvent>)events);

        var result = await _actions.ListActivities();

        Assert.Contains("1 activity",       result);
        Assert.Contains("=== run (1) ===",  result);
        Assert.Contains("30 minutes",       result);
    }

    [Fact]
    public async Task ListActivities_GroupsEventsByType_OrderedByCountDescending()
    {
        var events = new List<ActivityEvent>
                     {
                             new() { ActivityType = "run",        OccurredUtc = DateTimeOffset.UtcNow            }
                           , new() { ActivityType = "meditation", OccurredUtc = DateTimeOffset.UtcNow.AddDays(-1) }
                           , new() { ActivityType = "meditation", OccurredUtc = DateTimeOffset.UtcNow.AddDays(-2) }
                           , new() { ActivityType = "meditation", OccurredUtc = DateTimeOffset.UtcNow.AddDays(-3) }
                     };

        _logMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ActivityEvent>)events);

        var result = await _actions.ListActivities();

        Assert.Contains("4 activities", result);

        var meditationIndex = result.IndexOf("=== meditation (3) ===", StringComparison.Ordinal);
        var runIndex        = result.IndexOf("=== run (1) ===",        StringComparison.Ordinal);

        Assert.True(meditationIndex >= 0);
        Assert.True(runIndex        >= 0);
        Assert.True(meditationIndex < runIndex, "meditation group (3) should appear before run group (1)");
    }

    [Fact]
    public async Task ListActivities_IncludesNotesAndTags_WhenPresent()
    {
        var events = new List<ActivityEvent>
                     {
                             new()
                             {
                                     ActivityType = "run"
                                   , OccurredUtc  = DateTimeOffset.UtcNow
                                   , Notes        = "felt strong"
                                   , Tags         = new List<string> { "outdoors", "morning" }
                             }
                     };

        _logMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ActivityEvent>)events);

        var result = await _actions.ListActivities();

        Assert.Contains("felt strong",       result);
        Assert.Contains("outdoors, morning", result);
    }

    [Fact]
    public async Task ListActivities_DefaultsToLastSevenDays_WhenNoDatesProvided()
    {
        DateTimeOffset? capturedFrom = null;
        DateTimeOffset? capturedTo   = null;

        _logMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<DateTimeOffset?>()
                                          , It.IsAny<CancellationToken>()))
                .Callback<DateTimeOffset?, DateTimeOffset?, CancellationToken>((from, to, _) =>
                {
                    capturedFrom = from;
                    capturedTo   = to;
                })
                .ReturnsAsync((IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>());

        await _actions.ListActivities();

        Assert.NotNull(capturedFrom);
        Assert.Null(capturedTo);

        var approximatelySevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);
        Assert.InRange(capturedFrom!.Value
                     , approximatelySevenDaysAgo.AddMinutes(-5)
                     , approximatelySevenDaysAgo.AddMinutes(5));
    }
}
