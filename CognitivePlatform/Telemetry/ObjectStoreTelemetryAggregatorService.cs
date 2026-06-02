using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Reads persisted <see cref="TelemetryRecord"/> entries from the Object Store
/// and computes per-operation aggregate metrics. Survives restarts because
/// <see cref="PersistentConversationTelemetrySink"/> continuously writes records.
/// </summary>
public sealed class ObjectStoreTelemetryAggregatorService : ITelemetryAggregatorService
{
    private readonly IObjectStore _store;

    private const string PartitionKey = "telemetry";

    public ObjectStoreTelemetryAggregatorService(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<List<TelemetryMetricsResponse>> GetAggregatedMetricsAsync()
    {
        var records = _store.List<TelemetryRecord>(partitionKey: PartitionKey);

        var grouped = records
            .GroupBy(record => record.EventName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var list     = group.ToList();
                var count    = list.Count;
                var durations = list.Select(record => record.DurationMs).ToList();
                var successes = list.Count(record => record.Success);

                return new TelemetryMetricsResponse
                       {
                               OperationName     = group.Key
                             , Count             = count
                             , AverageDurationMs = count > 0 ? durations.Average() : 0
                             , MinDurationMs     = count > 0 ? durations.Min()     : 0
                             , MaxDurationMs     = count > 0 ? durations.Max()     : 0
                             , SuccessRate       = count > 0 ? (double)successes / count : 0
                             , LastActivity      = list.Max(record => record.TimestampUtc)
                       };
            })
            .OrderBy(metrics => metrics.OperationName)
            .ToList();

        return Task.FromResult(grouped);
    }
}
