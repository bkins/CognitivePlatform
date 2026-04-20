using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Tests;

public class TelemetryAggregatorServiceTests
{
    private readonly TelemetryAggregatorService _service = new();

    [Fact]
    public async Task GetAggregatedMetricsAsync_ReturnsEmptyList_WhenNoDataSourceWired()
    {
        var result = await _service.GetAggregatedMetricsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_ReturnsList_NotNull()
    {
        var result = await _service.GetAggregatedMetricsAsync();

        Assert.IsType<List<TelemetryMetricsResponse>>(result);
    }
}
