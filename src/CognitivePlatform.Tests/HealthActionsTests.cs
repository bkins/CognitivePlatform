using CognitivePlatform.Api.Health;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class HealthActionsTests
{
    private readonly Mock<IHealthProvider> _healthProviderMock = new();
    private readonly HealthActions _actions;

    public HealthActionsTests()
    {
        _actions = new HealthActions(_healthProviderMock.Object);
    }

    [Fact]
    public async Task GetStepCountAsync_ReturnsFormattedMessage_WhenMetricsExist()
    {
        var expectedDate = DateTime.UtcNow.Date;
        var metrics = new HealthMetricsDto
        {
            Steps = 8450,
            DistanceKm = 6.2,
            CaloriesBurned = 450,
            AverageHeartRate = 72,
            Date = expectedDate
        };

        _healthProviderMock
            .Setup(provider => provider.GetDailySummaryAsync(expectedDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        var result = await _actions.GetStepCountAsync("today");

        Assert.NotNull(result);
        Assert.Contains("8,450 steps", result.Message);
        Assert.Contains("6.20 km", result.Message);
    }

    [Fact]
    public async Task GetStepCountAsync_ReturnsFallbackMessage_WhenProviderReturnsNull()
    {
        _healthProviderMock
            .Setup(provider => provider.GetDailySummaryAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HealthMetricsDto?)null);

        var result = await _actions.GetStepCountAsync("today");

        Assert.NotNull(result);
        Assert.Contains("No step count data available", result.Message);
    }

    [Fact]
    public async Task GetSleepDataAsync_ReturnsFormattedMessage_WhenSleepDataExists()
    {
        var expectedDate = DateTime.UtcNow.Date;
        var sleep = new SleepSummaryDto
        {
            TotalSleepHours = 7.5,
            DeepSleepHours = 2.0,
            RemSleepHours = 1.8,
            LightSleepHours = 3.7,
            Date = expectedDate
        };

        _healthProviderMock
            .Setup(provider => provider.GetSleepSummaryAsync(expectedDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sleep);

        var result = await _actions.GetSleepDataAsync("today");

        Assert.NotNull(result);
        Assert.Contains("Total 7.5 hrs", result.Message);
        Assert.Contains("Deep: 2.0 hrs", result.Message);
    }
}
