using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

public interface IInsightHistoryStore
{
    Task RecordEmittedAsync( IReadOnlyList<Insight> insights
                           , CancellationToken      ct = default );

    Task<bool> WasRecentlyEmittedAsync( string            deduplicationKey
                                      , TimeSpan           window
                                      , CancellationToken  ct = default );

    Task RecordOutcomeAsync( string            deduplicationKey
                           , InsightOutcome    outcome
                           , CancellationToken ct = default );
}
