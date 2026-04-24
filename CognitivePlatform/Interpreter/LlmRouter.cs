using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Conversation;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Pure dispatcher. Reads SessionProviderKey from context.Metadata to pick the
/// active LlmProvider (falling back to the factory's configured default),
/// reads SessionModelKey to pick the model (falling back to the provider's
/// configured default), then forwards to the concrete ILlmClient resolved
/// via LlmClientFactory.
///
/// Stateless — a new client is created per call so that a mid-session
/// SetProvider switch takes effect on the next turn without any
/// re-initialisation.
/// </summary>
public class LlmRouter : ILlmRouter
{
    private readonly ILlmClientFactory   _factory;
    private readonly LlmProviderDefaults _defaults;

    public LlmRouter( ILlmClientFactory               factory
                    , IOptions<LlmProviderDefaults>   defaults )
    {
        _factory  = factory;
        _defaults = defaults.Value;
    }

    public Task<string> SendAsync( string              prompt
                                 , ConversationContext context
                                 , CancellationToken   ct = default)
    {
        var (client, model) = Resolve(context);
        return client.SendAsync(prompt, model, ct);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string                                     prompt
      , ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (client, model) = Resolve(context);

        await foreach (var chunk in client.StreamAsync(prompt, model, ct))
            yield return chunk;
    }

    private (ILlmClient client, string? model) Resolve(ConversationContext context)
    {
        var provider = ResolveProvider(context);
        var model    = ResolveModel(context, provider);
        var client   = _factory.Create(provider);

        return (client, model);
    }

    private LlmProvider ResolveProvider(ConversationContext context)
    {
        if (context.Metadata.TryGetValue(LlmActions.SessionProviderKey, out var raw)
         && Enum.TryParse<LlmProvider>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return _factory.DefaultProvider;
    }

    private string? ResolveModel(ConversationContext context, LlmProvider provider)
    {
        // Per-turn override (orchestrator populates this from request.Model) wins
        // over session-level preference.
        if (context.Metadata.TryGetValue("model", out var perTurnModel)
         && !string.IsNullOrWhiteSpace(perTurnModel))
        {
            return perTurnModel;
        }

        if (context.Metadata.TryGetValue(LlmActions.SessionModelKey, out var sessionModel)
         && !string.IsNullOrWhiteSpace(sessionModel))
        {
            return sessionModel;
        }

        return _defaults.For(provider);
    }
}
