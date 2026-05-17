using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Cross-session deduplication and recall surface for the Insight Engine.
///
/// Phase A: in-memory, process-scoped (see <see cref="InMemoryInsightHistoryStore"/>);
/// restart clears history. Phase B persists to the Object Store via
/// <see cref="ObjectStoreInsightHistoryStore"/>. Outcome tracking
/// (ActedOn / Dismissed / Expired) is activated in Phase B.
/// </summary>
public interface IInsightHistoryStore
{
    Task RecordEmittedAsync( IReadOnlyList<Insight> insights
                           , CancellationToken      cancellationToken = default );

    Task<bool> WasRecentlyEmittedAsync( string            deduplicationKey
                                      , TimeSpan          window
                                      , CancellationToken cancellationToken = default );

    /// <summary>
    /// Marks the most recent unresolved <see cref="InsightHistoryItem"/> for
    /// <paramref name="deduplicationKey"/> with the given <paramref name="outcome"/>.
    /// No-ops if no matching unresolved item exists.
    /// </summary>
    Task RecordOutcomeAsync( string            deduplicationKey
                           , InsightOutcome    outcome
                           , CancellationToken cancellationToken = default );

    /// <summary>
    /// Returns history items emitted within the given window, newest first.
    /// </summary>
    Task<IReadOnlyList<InsightHistoryItem>> GetRecentAsync( TimeSpan          window
                                                          , CancellationToken cancellationToken = default );
}
