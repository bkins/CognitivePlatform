using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Evaluates the Insight Engine without a live conversation turn — intended for
/// app-open or ambient notification triggers. Callers receive raw <see cref="Insight"/>
/// objects; no LLM weave pass is performed.
/// </summary>
public interface INotificationEngine
{
    /// <summary>
    /// Runs all registered insight providers against a lightweight context (no
    /// conversation history) and returns the survivors after deduplication and
    /// throttling. Emitted insights are recorded via <see cref="IInsightHistoryStore"/>.
    /// </summary>
    Task<IReadOnlyList<Insight>> EvaluateAsync( string?           sessionId
                                              , DateTime          now
                                              , CancellationToken ct = default );
}
