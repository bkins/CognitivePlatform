using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase B history store. Persists each emitted insight as an
/// <see cref="InsightHistoryItem"/> row in the Object Store, enabling
/// cross-session deduplication and outcome tracking that survive process restarts.
///
/// <para>
/// Partition key: <c>"insight-history"</c>. All reads use the Object Store's
/// <c>List</c> method (which filters out soft-deleted rows automatically).
/// Soft deletes are supported via <see cref="InsightHistoryItem.IsDeleted"/> /
/// <see cref="InsightHistoryItem.DeletedUtc"/> even though no code path
/// hard-deletes insight history today.
/// </para>
///
/// <para>
/// <see cref="RecordOutcomeAsync"/>: finds the most recent unresolved
/// (<see cref="InsightOutcome.Unknown"/>) row for the given key and updates it
/// via an upsert (same Id) rather than an in-place SQL UPDATE, so the Object
/// Store's typed-JSON abstraction is not circumvented.
/// </para>
/// </summary>
public sealed class ObjectStoreInsightHistoryStore : IInsightHistoryStore
{
    private readonly IObjectStore _store;

    private const string PartitionKey = "insight-history";

    public ObjectStoreInsightHistoryStore(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task RecordEmittedAsync( IReadOnlyList<Insight> insights
                                        , CancellationToken      cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var insight in insights)
        {
            if (insight.DeduplicationKey.HasNoValue())
                continue;

            var item = new InsightHistoryItem
                       {
                               InsightKey   = insight.DeduplicationKey
                             , Category     = insight.Category
                             , EmittedAtUtc = DateTime.UtcNow
                             , Message      = insight.Message
                             , Reasoning    = insight.Reasoning
                       };

            await _store.Save(item, partitionKey: PartitionKey, id: item.Id);
        }
    }

    public async Task<bool> WasRecentlyEmittedAsync( string            deduplicationKey
                                                     , TimeSpan          window
                                                     , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (deduplicationKey.HasNoValue())
            return false;

        var cutoff  = DateTime.UtcNow - window;
        var fromUtc = new DateTimeOffset(cutoff, TimeSpan.Zero);

        // Pre-filter by DB CreatedUtc ≈ EmittedAtUtc; then confirm in memory
        // using the JSON field to handle any sub-millisecond skew between them.
        var candidates = await _store.ListAsync<InsightHistoryItem>( partitionKey:      PartitionKey
                                                                    , fromUtc:           fromUtc
                                                                    , cancellationToken: cancellationToken );

        return candidates.Any(item =>
            string.Equals(item.InsightKey, deduplicationKey, StringComparison.Ordinal)
         && item.EmittedAtUtc >= cutoff);
    }

    public async Task RecordOutcomeAsync( string            deduplicationKey
                                        , InsightOutcome    outcome
                                        , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (deduplicationKey.HasNoValue())
            return;

        var all = await _store.ListAsync<InsightHistoryItem>( partitionKey:      PartitionKey
                                                             , cancellationToken: cancellationToken );

        var target = all
            .Where(item => string.Equals(item.InsightKey, deduplicationKey, StringComparison.Ordinal)
                        && item.Outcome == InsightOutcome.Unknown)
            .OrderByDescending(item => item.EmittedAtUtc)
            .FirstOrDefault();

        if (target is null)
            return;

        target.Outcome       = outcome;
        target.ResolvedAtUtc = DateTime.UtcNow;

        await _store.Save(target, partitionKey: PartitionKey, id: target.Id);
    }

    public async Task<IReadOnlyList<InsightHistoryItem>> GetRecentAsync( TimeSpan          window
                                                                       , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cutoff  = DateTime.UtcNow - window;
        var fromUtc = new DateTimeOffset(cutoff, TimeSpan.Zero);

        var raw = await _store.ListAsync<InsightHistoryItem>( partitionKey:      PartitionKey
                                                             , fromUtc:           fromUtc
                                                             , cancellationToken: cancellationToken );

        IReadOnlyList<InsightHistoryItem> recent = raw
            .Where(item => item.EmittedAtUtc >= cutoff)
            .OrderByDescending(item => item.EmittedAtUtc)
            .ToList();

        return recent;
    }
}
