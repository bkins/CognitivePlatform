using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Session-aware dispatcher in front of ILlmClient. Picks the active provider
/// and model from <see cref="ConversationContext.CurrentLlmSession"/> and
/// forwards to the concrete client resolved via LlmClientFactory.
///
/// No state of its own â€” every call re-reads the session so mid-session
/// provider switches take effect on the very next turn.
/// </summary>
public interface ILlmRouter
{
    Task<LlmResponse> SendAsync (string              prompt
                              , ConversationContext context
                              , TaskComplexity      complexity = TaskComplexity.Standard
                              , CancellationToken   ct         = default);

    IAsyncEnumerable<string> StreamAsync (string              prompt
                                        , ConversationContext context
                                        , CancellationToken   ct = default);

    /// <summary>
    /// Polishes <paramref name="originalResponse"/> by weaving the supplied insights
    /// in as conversational follow-on sentences. Per-session provider/model selection
    /// (BUG-14 / DEFERRED #10) is honored â€” the weave call goes through the same
    /// dispatch as a normal turn.
    ///
    /// The router builds the weave prompt internally and forwards via SendAsync;
    /// callers are not required to construct the prompt themselves.
    /// </summary>
    Task<string> WeaveAsync (ConversationContext    context
                           , string                 originalResponse
                           , IReadOnlyList<Insight> insights
                           , CancellationToken      cancellationToken = default);
}
