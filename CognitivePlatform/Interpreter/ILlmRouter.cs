using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Session-aware dispatcher in front of ILlmClient. Picks the active provider
/// and model from <see cref="ConversationContext.CurrentLlmSession"/> and
/// forwards to the concrete client resolved via LlmClientFactory.
///
/// No state of its own — every call re-reads the session so mid-session
/// provider switches take effect on the very next turn.
/// </summary>
public interface ILlmRouter
{
    Task<string> SendAsync (string              prompt
                          , ConversationContext context
                          , CancellationToken   ct = default);

    IAsyncEnumerable<string> StreamAsync (string              prompt
                                        , ConversationContext context
                                        , CancellationToken   ct = default);
}
