using CognitivePlatform.Api.Domains.Wellbeing;
using CognitivePlatform.Api.Wellbeing;
using Moq;

namespace CognitivePlatform.Tests.Wellbeing;

public sealed class WellbeingActionsTests
{
    private readonly Mock<IWellbeingPatternService> _patternServiceMock = new();
    private readonly WellbeingActions               _actions;

    private static readonly DateOnly AnyFrom = new(2026, 5, 23);
    private static readonly DateOnly AnyTo   = new(2026, 5, 29);

    public WellbeingActionsTests()
    {
        _actions = new WellbeingActions(_patternServiceMock.Object);
    }

    // ====================================================================
    // GetWellbeingReport
    // ====================================================================

    [Fact]
    public async Task GetWellbeingReport_ReturnsNarrativeSummary_WhenDataAvailable()
    {
        var report = BuildReport("Your sleep and productivity look good this week.");
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.GetWellbeingReport("last week");

        Assert.Contains("Your sleep and productivity look good this week.", result);
    }

    [Fact]
    public async Task GetWellbeingReport_UsesDefaultSevenDayRange_WhenDateRangeIsEmpty()
    {
        var report = BuildReport("No notable patterns.");
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.GetWellbeingReport(string.Empty);

        Assert.NotEmpty(result);
        _patternServiceMock.Verify(service => service.AnalyseAsync(
            It.IsAny<DateOnly>()
          , It.IsAny<DateOnly>()
          , It.IsAny<CancellationToken>())
        , Times.Once);
    }

    [Fact]
    public async Task GetWellbeingReport_ReturnsFriendlyMessage_WhenServiceThrows()
    {
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ThrowsAsync(new InvalidOperationException("store unavailable"));

        var result = await _actions.GetWellbeingReport("last week");

        Assert.Contains("wasn't able to generate", result);
    }

    [Fact]
    public async Task GetWellbeingReport_DoesNotPropagate_WhenServiceThrows()
    {
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ThrowsAsync(new TimeoutException());

        var exception = await Record.ExceptionAsync(() => _actions.GetWellbeingReport("last week"));

        Assert.Null(exception);
    }

    // ====================================================================
    // GetWellbeingPatterns
    // ====================================================================

    [Fact]
    public async Task GetWellbeingPatterns_ReturnsFormattedList_WhenPatternsExist()
    {
        var report = BuildReport(patterns:
        [
            new WellbeingPattern
            {
                Name        = "SleepTaskCorrelation"
              , Description = "Sleep and task rate are both low."
              , Severity    = PatternSeverity.Concern
            }
        ]);
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.GetWellbeingPatterns("last week");

        Assert.Contains("Patterns detected",              result);
        Assert.Contains("Sleep and task rate are both low.", result);
        Assert.Contains("Concern",                         result);
    }

    [Fact]
    public async Task GetWellbeingPatterns_ReturnsFriendlyMessage_WhenNoPatternsDetected()
    {
        var report = BuildReport(patterns: []);
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.GetWellbeingPatterns(string.Empty);

        Assert.Contains("No notable patterns detected", result);
    }

    [Fact]
    public async Task GetWellbeingPatterns_ReturnsFriendlyMessage_WhenServiceThrows()
    {
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ThrowsAsync(new InvalidOperationException());

        var result = await _actions.GetWellbeingPatterns("last week");

        Assert.Contains("wasn't able to retrieve", result);
    }

    // ====================================================================
    // CheckSleepHealth
    // ====================================================================

    [Fact]
    public async Task CheckSleepHealth_ReturnsConcernDescription_WhenSleepConcernPatternExists()
    {
        var report = BuildReport(patterns:
        [
            new WellbeingPattern
            {
                Name        = "SleepTaskCorrelation"
              , Description = "Average sleep is 4.8h — below the healthy threshold."
              , Severity    = PatternSeverity.Concern
            }
        ]);
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.CheckSleepHealth("last week");

        Assert.Contains("Average sleep is 4.8h", result);
    }

    [Fact]
    public async Task CheckSleepHealth_ReturnsNoSleepConcerns_WhenNoSleepPatternsExist()
    {
        var report = BuildReport(patterns:
        [
            new WellbeingPattern
            {
                Name        = "StepCountTrend"
              , Description = "Step count is down 25% from last period."
              , Severity    = PatternSeverity.Attention
            }
        ]);
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.CheckSleepHealth("last week");

        Assert.Contains("No sleep concerns detected", result);
    }

    [Fact]
    public async Task CheckSleepHealth_ReturnsNoSleepConcerns_WhenPatternListIsEmpty()
    {
        var report = BuildReport(patterns: []);
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ReturnsAsync(report);

        var result = await _actions.CheckSleepHealth(string.Empty);

        Assert.Contains("No sleep concerns detected", result);
    }

    [Fact]
    public async Task CheckSleepHealth_ReturnsFriendlyMessage_WhenServiceThrows()
    {
        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .ThrowsAsync(new InvalidOperationException());

        var result = await _actions.CheckSleepHealth("last week");

        Assert.Contains("wasn't able to check sleep health", result);
    }

    // ====================================================================
    // Date range resolution
    // ====================================================================

    [Fact]
    public async Task GetWellbeingReport_CallsAnalyseAsync_WithWiderRange_WhenMonthRangeRequested()
    {
        var report = BuildReport("Monthly snapshot.");
        DateOnly capturedFrom = default;
        DateOnly capturedTo   = default;

        _patternServiceMock.Setup(service => service.AnalyseAsync(It.IsAny<DateOnly>()
                                                                , It.IsAny<DateOnly>()
                                                                , It.IsAny<CancellationToken>()))
                           .Callback<DateOnly, DateOnly, CancellationToken>((from, to, _) =>
                           {
                               capturedFrom = from;
                               capturedTo   = to;
                           })
                           .ReturnsAsync(report);

        await _actions.GetWellbeingReport("last 30 days");

        var daySpan = capturedTo.DayNumber - capturedFrom.DayNumber;
        Assert.True(daySpan >= 28, $"Expected ≥28-day range for 'last 30 days', got {daySpan}");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static WellbeingReport BuildReport(
        string narrative         = "All signals nominal."
      , WellbeingPattern[]? patterns = null)
        => new()
           {
               From             = AnyFrom
             , To               = AnyTo
             , Patterns         = patterns ?? []
             , NarrativeSummary = narrative
           };
}
