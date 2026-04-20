namespace CognitivePlatform.Api.Telemetry;

public interface ITelemetryAggregatorService
{
    Task<List<TelemetryMetricsResponse>> GetAggregatedMetricsAsync();
}
