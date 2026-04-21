using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase A stub — no persistence. WasRecentlyEmittedAsync always returns false so
/// every insight passes deduplication. Replaced by ObjectStoreInsightHistoryStore in Phase B.
/// </summary>
public sealed class NoOpInsightHistoryStore : IInsightHistoryStore
{
    public Task RecordEmittedAsync( IReadOnlyList<Insight> insights
                                  , CancellationToken      ct = default )
        => Task.CompletedTask;

    public Task<bool> WasRecentlyEmittedAsync( string            deduplicationKey
                                             , TimeSpan           window
                                             , CancellationToken  ct = default )
        => Task.FromResult(false);

    public Task RecordOutcomeAsync( string            deduplicationKey
                                  , InsightOutcome    outcome
                                  , CancellationToken ct = default )
        => Task.CompletedTask;
}
