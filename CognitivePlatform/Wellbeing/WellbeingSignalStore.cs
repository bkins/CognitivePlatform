using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Wellbeing;

/// <summary>
/// Persists <see cref="WellbeingSignal"/> records via <see cref="IObjectStore"/>.
/// Each calendar day gets its own partition: <c>"wellbeing.signal.{yyyy-MM-dd}"</c>.
/// The signal's stable Id (<c>"{Source}.{MetricName}"</c>) ensures that re-collecting
/// the same metric for the same date upserts rather than duplicates.
/// </summary>
public sealed class WellbeingSignalStore : IWellbeingSignalStore
{
    private readonly IObjectStore _store;

    public WellbeingSignalStore(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task SaveSignalAsync(WellbeingSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Save(signal, partitionKey: PartitionKey(signal.Date), id: signal.Id);
    }

    public Task<IReadOnlyList<WellbeingSignal>> GetSignalsForDateAsync(
        DateOnly          date
      , CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<WellbeingSignal> signals = _store.List<WellbeingSignal>(partitionKey: PartitionKey(date));

        return Task.FromResult(signals);
    }

    public Task<IReadOnlyList<WellbeingSignal>> GetSignalsAsync(
        DateTimeOffset    from
      , DateTimeOffset    to
      , CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fromDate = DateOnly.FromDateTime(from.UtcDateTime);
        var toDate   = DateOnly.FromDateTime(to.UtcDateTime);

        var result = new List<WellbeingSignal>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            result.AddRange(_store.List<WellbeingSignal>(partitionKey: PartitionKey(date)));

        return Task.FromResult<IReadOnlyList<WellbeingSignal>>(result);
    }

    private static string PartitionKey(DateOnly date) => $"wellbeing.signal.{date:yyyy-MM-dd}";
}
