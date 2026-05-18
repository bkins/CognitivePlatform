using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase D notification evaluator. Invoked by the caller (e.g. the MAUI app-open
/// event) to generate ambient insights without a live conversation turn.
///
/// <para>
/// Differences from the in-turn <see cref="InsightEngine"/>:
/// <list type="bullet">
///   <item>No conversation history — providers receive a minimal <see cref="ConversationContext"/>.</item>
///   <item>No LLM weave pass — insights are returned as raw objects.</item>
///   <item>No background scheduling or timers — the caller decides when to invoke.</item>
/// </list>
/// All deduplication and throttling still apply because the same
/// <see cref="IInsightEngine"/> (and its injected <see cref="IInsightHistoryStore"/>)
/// is used under the hood.
/// </para>
/// </summary>
public sealed class NotificationEngine : INotificationEngine
{
    private readonly IInsightEngine       _insightEngine;
    private readonly IInsightHistoryStore _historyStore;

    public NotificationEngine( IInsightEngine       insightEngine
                              , IInsightHistoryStore historyStore )
    {
        _insightEngine = insightEngine ?? throw new ArgumentNullException(nameof(insightEngine));
        _historyStore  = historyStore  ?? throw new ArgumentNullException(nameof(historyStore));
    }

    public async Task<IReadOnlyList<Insight>> EvaluateAsync( string?           sessionId
                                                           , DateTime          now
                                                           , CancellationToken ct = default )
    {
        var context = new ConversationContext(sessionId ?? Guid.NewGuid().ToString());

        var insights = await _insightEngine.GenerateInsightsAsync(context, ct);

        if (insights.Count > 0)
            await _historyStore.RecordEmittedAsync(insights, ct);

        return insights;
    }
}
