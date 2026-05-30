using Moq;
using CognitivePlatform.Api.Domains.Health;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Integrations.Health.Models;

namespace CognitivePlatform.Tests;

public class HealthActionsTests
{
    private readonly Mock<IHealthProvider> _healthMock = new();
    private readonly HealthActions         _actions;

    public HealthActionsTests()
    {
        _actions = new HealthActions(_healthMock.Object);
    }

    // ================================================================
    // GetStepCount
    // ================================================================

    [Fact]
    public async Task GetStepCount_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.GetStepCount("yesterday");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStepCount_ReturnsParseError_WhenDateRangeIsInvalid()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.GetStepCount("not-a-date");

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStepCount_ReturnsStepData_WhenProviderConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetStepCountAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new StepCountResult { Steps = 8432 });

        var result = await _actions.GetStepCount("yesterday");

        Assert.Contains("Steps",  result);
        Assert.Contains("8",      result);
    }

    [Fact]
    public async Task GetStepCount_IncludesDistanceAndCalories_WhenOptionalFieldsPresent()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetStepCountAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new StepCountResult
                                 {
                                     Steps          = 10000
                                   , DistanceMetres = 7500
                                   , ActiveCalories = 350
                                 });

        var result = await _actions.GetStepCount("today");

        Assert.Contains("km",   result);
        Assert.Contains("kcal", result);
    }

    [Fact]
    public async Task GetStepCount_QueriesLastSevenDays_WhenDateRangeIsLastWeek()
    {
        DateTimeOffset capturedFrom = default;
        DateTimeOffset capturedTo   = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetStepCountAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, to, _) =>
                   {
                       capturedFrom = from;
                       capturedTo   = to;
                   })
                   .ReturnsAsync(new StepCountResult { Steps = 50000 });

        await _actions.GetStepCount("last week");

        Assert.True((capturedTo - capturedFrom).TotalDays >= 7);
    }

    [Fact]
    public async Task GetStepCount_QueriesYesterdayWindow_WhenDateRangeIsYesterday()
    {
        DateTimeOffset capturedFrom = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetStepCountAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => capturedFrom = from)
                   .ReturnsAsync(new StepCountResult { Steps = 9000 });

        await _actions.GetStepCount("yesterday");

        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(-1), capturedFrom.LocalDateTime.Date);
    }

    [Fact]
    public async Task GetStepCount_QueriesTodayWindow_WhenDateRangeIsToday()
    {
        DateTimeOffset capturedFrom = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetStepCountAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => capturedFrom = from)
                   .ReturnsAsync(new StepCountResult { Steps = 5000 });

        await _actions.GetStepCount("today");

        Assert.Equal(DateTimeOffset.UtcNow.Date, capturedFrom.LocalDateTime.Date);
    }

    // ================================================================
    // GetSleepSummary
    // ================================================================

    [Fact]
    public async Task GetSleepSummary_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.GetSleepSummary("yesterday");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSleepSummary_ReturnsParseError_WhenDateRangeIsInvalid()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.GetSleepSummary("not-a-date");

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSleepSummary_ReturnsTotalHoursAndMinutes_WhenProviderConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetSleepAsync( It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new SleepResult { TotalMinutes = 480, Sessions = 1 });

        var result = await _actions.GetSleepSummary("yesterday");

        Assert.Contains("8h", result);
        Assert.Contains("0m", result);
    }

    [Fact]
    public async Task GetSleepSummary_IncludesPhaseBreakdown_WhenOptionalFieldsPresent()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetSleepAsync( It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new SleepResult
                                 {
                                     TotalMinutes = 480
                                   , DeepMinutes  = 90
                                   , RemMinutes   = 120
                                   , LightMinutes = 270
                                   , Sessions     = 1
                                 });

        var result = await _actions.GetSleepSummary("yesterday");

        Assert.Contains("deep",  result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REM",   result);
        Assert.Contains("light", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSleepSummary_IncludesSessionCount_WhenMultipleSessions()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetSleepAsync( It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new SleepResult { TotalMinutes = 360, Sessions = 2 });

        var result = await _actions.GetSleepSummary("yesterday");

        Assert.Contains("2 sessions", result);
    }

    [Fact]
    public async Task GetSleepSummary_QueriesYesterdayWindow_WhenDateRangeIsYesterday()
    {
        DateTimeOffset capturedFrom = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetSleepAsync( It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<DateTimeOffset>()
                                                            , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => capturedFrom = from)
                   .ReturnsAsync(new SleepResult { TotalMinutes = 420, Sessions = 1 });

        await _actions.GetSleepSummary("yesterday");

        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(-1), capturedFrom.LocalDateTime.Date);
    }

    // ================================================================
    // GetHeartRate
    // ================================================================

    [Fact]
    public async Task GetHeartRate_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.GetHeartRate("yesterday");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHeartRate_ReturnsParseError_WhenDateRangeIsInvalid()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.GetHeartRate("not-a-date");

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHeartRate_ReturnsAverageBpm_WhenProviderConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetHeartRateAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new HeartRateResult { AverageBpm = 72 });

        var result = await _actions.GetHeartRate("yesterday");

        Assert.Contains("72",  result);
        Assert.Contains("bpm", result);
    }

    [Fact]
    public async Task GetHeartRate_IncludesRangeAndResting_WhenOptionalFieldsPresent()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetHeartRateAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new HeartRateResult
                                 {
                                     AverageBpm = 72
                                   , MinBpm     = 55
                                   , MaxBpm     = 145
                                   , RestingBpm = 58
                                 });

        var result = await _actions.GetHeartRate("today");

        Assert.Contains("55",      result);
        Assert.Contains("145",     result);
        Assert.Contains("resting", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHeartRate_QueriesLastSevenDays_WhenDateRangeIsLastWeek()
    {
        DateTimeOffset capturedFrom = default;
        DateTimeOffset capturedTo   = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetHeartRateAsync( It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<DateTimeOffset>()
                                                                 , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, to, _) =>
                   {
                       capturedFrom = from;
                       capturedTo   = to;
                   })
                   .ReturnsAsync(new HeartRateResult { AverageBpm = 68 });

        await _actions.GetHeartRate("last week");

        Assert.True((capturedTo - capturedFrom).TotalDays >= 7);
    }

    // ================================================================
    // GetDistance
    // ================================================================

    [Fact]
    public async Task GetDistance_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.GetDistance("yesterday");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDistance_ReturnsParseError_WhenDateRangeIsInvalid()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.GetDistance("not-a-date");

        Assert.Contains("couldn't parse", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDistance_ReturnsKilometers_WhenProviderConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetDistanceAsync( It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new DistanceResult { Metres = 5000 });

        var result = await _actions.GetDistance("yesterday");

        Assert.Contains("km",  result);
        Assert.Contains("5.0", result);
    }

    [Fact]
    public async Task GetDistance_IncludesStepsAndCalories_WhenOptionalFieldsPresent()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetDistanceAsync( It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new DistanceResult
                                 {
                                     Metres         = 8000
                                   , Steps          = 9800
                                   , ActiveCalories = 420
                                 });

        var result = await _actions.GetDistance("today");

        Assert.Contains("steps", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kcal",  result);
    }

    [Fact]
    public async Task GetDistance_QueriesLastSevenDays_WhenDateRangeIsLastWeek()
    {
        DateTimeOffset capturedFrom = default;
        DateTimeOffset capturedTo   = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetDistanceAsync( It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, to, _) =>
                   {
                       capturedFrom = from;
                       capturedTo   = to;
                   })
                   .ReturnsAsync(new DistanceResult { Metres = 35000 });

        await _actions.GetDistance("last week");

        Assert.True((capturedTo - capturedFrom).TotalDays >= 7);
    }

    [Fact]
    public async Task GetDistance_QueriesTodayWindow_WhenDateRangeIsToday()
    {
        DateTimeOffset capturedFrom = default;

        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock.Setup(provider => provider.GetDistanceAsync( It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<DateTimeOffset>()
                                                               , It.IsAny<CancellationToken>()))
                   .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => capturedFrom = from)
                   .ReturnsAsync(new DistanceResult { Metres = 3200 });

        await _actions.GetDistance("today");

        Assert.Equal(DateTimeOffset.UtcNow.Date, capturedFrom.LocalDateTime.Date);
    }
}
