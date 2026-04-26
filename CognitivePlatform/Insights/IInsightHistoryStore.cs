using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Cross-session deduplication and recall surface for the Insight Engine.
///
/// Phase A: in-memory, process-scoped (see <see cref="InMemoryInsightHistoryStore"/>);
/// restart clears history. Phase B persists to the Object Store. Outcome tracking
/// (ActedOn / Dismissed / Expired) is deferred to Phase C.
/// </summary>
public interface IInsightHistoryStore
{
    Task RecordEmittedAsync( IReadOnlyList<Insight> insights
                           , CancellationToken      cancellationToken = default );

    Task<bool> WasRecentlyEmittedAsync( string             deduplicationKey
                                      , TimeSpan           window
                                      , CancellationToken  cancellationToken = default );

    /// <summary>
    /// Returns history items emitted within the given window, newest first.
    /// Shipped in Phase A even though no Phase A consumer uses it — forward-compat
    /// for the Phase E coaching layer.
    /// </summary>
    Task<IReadOnlyList<InsightHistoryItem>> GetRecentAsync( TimeSpan          window
                                                          , CancellationToken cancellationToken = default );
}
