namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Forward-compatible stub. Returns an empty list until a structured
/// telemetry event store is wired up in a future session.
/// </summary>
public class TelemetryAggregatorService : ITelemetryAggregatorService
{
    public Task<List<TelemetryMetricsResponse>> GetAggregatedMetricsAsync()
    {
        return Task.FromResult(new List<TelemetryMetricsResponse>());
    }
}
