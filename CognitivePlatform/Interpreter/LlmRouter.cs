using System.Runtime.CompilerServices;
using System.Text;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.SystemPromptLogging;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Pure dispatcher. Reads <see cref="ConversationContext.CurrentLlmSession"/>
/// to pick the active <see cref="LlmProvider"/> (falling back to the factory's
/// configured default) and the model (falling back to the provider's configured
/// default), then forwards to the concrete ILlmClient resolved via
/// LlmClientFactory.
///
/// Stateless — a new client is created per call so that a mid-session
/// SetProvider switch takes effect on the next turn without any
/// re-initialisation.
/// </summary>
public class LlmRouter : ILlmRouter
{
    private readonly ILlmClientFactory   _factory;
    private readonly LlmProviderDefaults _defaults;
    private readonly IPromptLogger       _promptLogger;

    public LlmRouter( ILlmClientFactory             factory
                    , IOptions<LlmProviderDefaults> defaults
                    , IPromptLogger                 promptLogger )
    {
        _factory      = factory;
        _defaults     = defaults.Value;
        _promptLogger = promptLogger;
    }

    public Task<string> SendAsync( string              prompt
                                 , ConversationContext context
                                 , CancellationToken   ct = default)
    {
        var (client, model) = Resolve(context);
        return client.SendAsync(prompt, model, ct);
    }

    public async IAsyncEnumerable<string> StreamAsync( string                                     prompt
                                                     , ConversationContext                        context
                                                     , [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (client, model) = Resolve(context);

        await foreach (var chunk in client.StreamAsync(prompt, model, ct))
            yield return chunk;
    }

    public Task<string> WeaveAsync( ConversationContext    context
                                  , string                 originalResponse
                                  , IReadOnlyList<Insight> insights
                                  , CancellationToken      cancellationToken = default )
    {
        var prompt = BuildWeavePrompt(originalResponse, insights);
        
        _promptLogger.Log("WeaveLlmRouter.WeaveAsync", prompt, _defaults.ToString());
        
        return SendAsync(prompt, context, cancellationToken);
    }

    private static string BuildWeavePrompt(string                 originalResponse
                                         , IReadOnlyList<Insight> insights)
    {
        var insightLines = string.Join(Environment.NewLine
                                     , insights.Select(insight => $"- {insight.Message}"));

        var prompt = new StringBuilder();
        prompt.AppendLine("You are a helpful assistant. Present the result below to the user first, then naturally");
        prompt.AppendLine("transition into the suggestions as conversational follow-on sentences — not as a bullet");
        prompt.AppendLine("list. The suggestions should feel like a thoughtful aside, not a notification.");
        prompt.AppendLine();
        prompt.AppendLine("Result:");
        prompt.AppendLine(originalResponse);
        prompt.AppendLine();
        prompt.AppendLine("Suggestions to weave in:");
        prompt.Append(insightLines);

        return prompt.ToString();
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
        var session = context.CurrentLlmSession;

        if (session.HasProvider
         && Enum.TryParse<LlmProvider>(session.Provider, ignoreCase: true, out var parsed))
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
         && perTurnModel.HasValue())
        {
            return perTurnModel;
        }

        var session = context.CurrentLlmSession;

        return session.HasModel
                       ? session.Model
                       : _defaults.For(provider);

    }
}
