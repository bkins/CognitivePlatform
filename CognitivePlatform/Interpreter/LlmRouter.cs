using System.Runtime.CompilerServices;
using System.Text;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.SystemPromptLogging;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Session-aware dispatcher in front of <see cref="ILlmClient"/>. Picks the
/// active provider and model from <see cref="ConversationContext.CurrentLlmSession"/>
/// and forwards to the concrete client resolved via <see cref="LlmClientFactory"/>.
///
/// After every successful <see cref="SendAsync"/> call the pipeline records
/// usage data via <see cref="ILlmUsageAggregator"/> and updates the per-provider
/// rate-limit state via <see cref="ILlmRateLimiter"/>.
///
/// Stateless — a new client is created per call so that a mid-session
/// SetProvider switch takes effect on the very next turn without re-initialisation.
/// </summary>
public class LlmRouter : ILlmRouter
{
    private readonly ILlmClientFactory   _factory;
    private readonly LlmProviderDefaults _defaults;
    private readonly IPromptLogger       _promptLogger;
    private readonly ILlmUsageAggregator _usageAggregator;
    private readonly ILlmRateLimiter     _rateLimiter;
    private readonly ILlmCapacityRouter  _capacityRouter;
    private readonly ILlmFallbackChain   _fallbackChain;
    private readonly ILogger<LlmRouter>  _logger;

    public LlmRouter( ILlmClientFactory   factory
                    , LlmProviderDefaults defaults
                    , IPromptLogger       promptLogger
                    , ILlmUsageAggregator usageAggregator
                    , ILlmRateLimiter     rateLimiter
                    , ILlmCapacityRouter  capacityRouter
                    , ILlmFallbackChain   fallbackChain
                    , ILogger<LlmRouter>? logger = null )
    {
        _factory          = factory;
        _defaults         = defaults;
        _promptLogger     = promptLogger;
        _usageAggregator  = usageAggregator;
        _rateLimiter      = rateLimiter;
        _capacityRouter   = capacityRouter;
        _fallbackChain    = fallbackChain;
        _logger           = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmRouter>.Instance;
    }

    public async Task<LlmResponse> SendAsync( string              prompt
                                            , ConversationContext context
                                            , TaskComplexity      complexity = TaskComplexity.Standard
                                            , CancellationToken   ct         = default )
    {
        var sessionProvider  = ResolveProvider(context);
        var sessionModel     = ResolveModel(context, sessionProvider);
        var client           = _factory.Create(sessionProvider);
        var resolvedModel    = sessionModel;
        var resolvedProvider = sessionProvider.ToString();
        string? switchNote         = null;
        string? tierDowngradeNote  = null;
        var diagnosticId = context.Metadata.GetValueOrDefault("diagnostic_id", "unavailable");

        // When the session-preferred provider is exhausted (as signalled by its
        // last rate-limit snapshot), delegate to the capacity router to find
        // the next non-exhausted model.  LlmCapacityExceededException propagates
        // up through the interpreter so the orchestrator can surface a friendly message.
        if (_rateLimiter.IsExhausted(sessionProvider.ToString()))
        {
            var capacity         = _capacityRouter.SelectModel(complexity);
            var capacityProvider = Enum.TryParse<LlmProvider>(capacity.ModelId.Provider, ignoreCase: true, out var parsed)
                                           ? parsed
                                           : _factory.DefaultProvider;

            client              = _factory.Create(capacityProvider);
            resolvedModel       = capacity.ModelId.Model;
            resolvedProvider    = capacity.ModelId.Provider;
            switchNote          = $"Switched to {capacity.ModelId.Provider} ({capacity.ModelId.Model}) — {sessionProvider} limit reached";
            tierDowngradeNote   = capacity.TierDowngradeNote;

            if (tierDowngradeNote != null)
                context.Metadata["tier_downgrade_note"] = tierDowngradeNote;
        }

        LlmResponse response;
        try
        {
            _logger.LogInformation("LLM provider attempt. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model} Phase={Phase}", diagnosticId, resolvedProvider, resolvedModel, "primary");
            response = await client.SendAsync(prompt, resolvedModel, ct);
            _logger.LogInformation("LLM provider succeeded. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model}", diagnosticId, resolvedProvider, resolvedModel);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                || ex is TimeoutException
                                || ex is TaskCanceledException)
        {
            _logger.LogWarning(ex, "LLM provider failed. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model} Reason={Reason}", diagnosticId, resolvedProvider, resolvedModel, FailureReason(ex, ct));
            // Primary provider failed. Walk the explicit fallback chain (if enabled) in
            // order, skipping entries that also fail. Fall back to the capacity router
            // when the chain is disabled or not configured.
            if (_fallbackChain.Enabled)
            {
                LlmResponse? fallbackResponse = null;
                var          fallbacks        = await _fallbackChain.GetViableFallbacksAsync(ct);

                foreach (var (fbProvider, fbModel) in fallbacks)
                {
                    var fbClient = _factory.Create(fbProvider);
                    try
                    {
                        _logger.LogInformation("LLM fallback attempt. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model}", diagnosticId, fbProvider, fbModel);
                        fallbackResponse  = await fbClient.SendAsync(prompt, fbModel, ct);
                        resolvedModel     = fbModel;
                        resolvedProvider  = fbProvider.ToString();
                        switchNote        = $"Switched to {fbProvider} ({fbModel}) — {sessionProvider} unavailable";
                        tierDowngradeNote = _fallbackChain.FallbackNote;
                        context.Metadata["tier_downgrade_note"] = tierDowngradeNote;
                        break;
                    }
                    catch (Exception fbEx) when (fbEx is HttpRequestException
                                              || fbEx is TimeoutException
                                              || fbEx is TaskCanceledException)
                    {
                        _logger.LogWarning(fbEx, "LLM fallback failed. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model} Reason={Reason}", diagnosticId, fbProvider, fbModel, FailureReason(fbEx, ct));
                    }
                }

                if (fallbackResponse is null)
                {
                    _logger.LogError("LLM providers exhausted. DiagnosticId={DiagnosticId} Reason={Reason}", diagnosticId, "all_attempts_failed");
                    return new LlmResponse
                           {
                                   Content    = UnavailableMessage(diagnosticId)
                                 , Usage      = LlmUsageInfo.Empty
                                 , RateLimits = LlmRateLimitSnapshot.Empty
                           };
                }

                response = fallbackResponse;
            }
            else
            {
                // No explicit fallback chain — delegate to the capacity router.
                var fallback         = _capacityRouter.SelectModel(complexity);
                var fallbackProvider = Enum.TryParse<LlmProvider>(fallback.ModelId.Provider, ignoreCase: true, out var fp)
                                               ? fp
                                               : _factory.DefaultProvider;

                client            = _factory.Create(fallbackProvider);
                resolvedModel     = fallback.ModelId.Model;
                resolvedProvider  = fallback.ModelId.Provider;
                switchNote        = $"Switched to {fallback.ModelId.Provider} ({fallback.ModelId.Model}) — {sessionProvider} unavailable";
                tierDowngradeNote = fallback.TierDowngradeNote;

                if (tierDowngradeNote is not null)
                    context.Metadata["tier_downgrade_note"] = tierDowngradeNote;

                try
                {
                    response = await client.SendAsync(prompt, resolvedModel, ct);
                }
                catch (Exception fbEx) when (fbEx is HttpRequestException
                                          || fbEx is TimeoutException
                                          || fbEx is TaskCanceledException)
                {
                    _logger.LogError(fbEx, "LLM fallback failed. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model} Reason={Reason}", diagnosticId, resolvedProvider, resolvedModel, FailureReason(fbEx, ct));
                    return new LlmResponse
                           {
                                   Content    = UnavailableMessage(diagnosticId)
                                 , Usage      = LlmUsageInfo.Empty
                                 , RateLimits = LlmRateLimitSnapshot.Empty
                           };
                }
            }
        }

        context.Metadata["resolved_provider"] = resolvedProvider;
        context.Metadata["resolved_model"]    = resolvedModel ?? string.Empty;

        // FIX: this used to also write context.Metadata["model"] = resolvedModel here, with
        // the stated intent of letting "training telemetry capture the real model... when a
        // fallback was performed." But ResolveModel (below) treats Metadata["model"] as a
        // *per-turn caller override that outranks the session's selected provider/model* —
        // and Metadata lives for the whole session, not just this turn, with no code anywhere
        // clearing it afterward. The result: the first time a fallback ever fired, this turn's
        // fallback model got permanently pinned as an override for every subsequent turn,
        // silently fighting any later SetProvider/SetModel call (the model name wouldn't even
        // belong to the newly selected provider). The telemetry goal is already satisfied by
        // LlmResponseMetadata below (ProviderId/ModelId/ProviderSwitchNote), which is the
        // correct, turn-scoped place for "what model actually served this response" — so the
        // Metadata write was both redundant and the root cause of provider/model switches not
        // sticking. Removed.
        var metadata = new LlmResponseMetadata
                       {
                               ProviderId        = resolvedProvider
                             , ModelId           = resolvedModel ?? string.Empty
                             , Usage             = response.Usage
                             , RateLimits        = response.RateLimits
                             , CapturedUtc       = DateTimeOffset.UtcNow
                             , ProviderSwitchNote = switchNote
                             , TierDowngradeNote  = tierDowngradeNote
                       };

        _usageAggregator.Record(metadata);
        _rateLimiter.Update(metadata);

        var modelId = new LlmModelId(resolvedProvider, resolvedModel ?? string.Empty);

        if (!ReferenceEquals(response.Usage, LlmUsageInfo.Empty))
            _capacityRouter.RecordUsage(modelId, response.Usage);

        if (response.RateLimits.HasData)
            _capacityRouter.RecordRateLimits(modelId, response.RateLimits);

        return response;
    }

    public async IAsyncEnumerable<string> StreamAsync( string                                     prompt
                                                     , ConversationContext                        context
                                                     , [EnumeratorCancellation] CancellationToken ct = default )
    {
        var sessionProvider  = ResolveProvider(context);
        var sessionModel     = ResolveModel(context, sessionProvider);
        var client           = _factory.Create(sessionProvider);
        var resolvedModel    = sessionModel;
        var resolvedProvider = sessionProvider.ToString();
        string? switchNote         = null;
        string? tierDowngradeNote  = null;
        var diagnosticId = context.Metadata.GetValueOrDefault("diagnostic_id", "unavailable");

        if (_rateLimiter.IsExhausted(sessionProvider.ToString()))
        {
            var capacity         = _capacityRouter.SelectModel(TaskComplexity.Standard);
            var capacityProvider = Enum.TryParse<LlmProvider>(capacity.ModelId.Provider, ignoreCase: true, out var parsed)
                                           ? parsed
                                           : _factory.DefaultProvider;

            client              = _factory.Create(capacityProvider);
            resolvedModel       = capacity.ModelId.Model;
            resolvedProvider    = capacity.ModelId.Provider;
            switchNote          = $"Switched to {capacity.ModelId.Provider} ({capacity.ModelId.Model}) — {sessionProvider} limit reached";
            tierDowngradeNote   = capacity.TierDowngradeNote;

            if (tierDowngradeNote != null)
                context.Metadata["tier_downgrade_note"] = tierDowngradeNote;
        }

        IAsyncEnumerator<string>? enumerator = null;
        try
        {
            _logger.LogInformation("LLM stream provider attempt. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model}", diagnosticId, resolvedProvider, resolvedModel);
            enumerator = client.StreamAsync(prompt, resolvedModel, ct).GetAsyncEnumerator(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                || ex is TimeoutException
                                || ex is TaskCanceledException)
        {
            _logger.LogWarning(ex, "LLM stream provider failed before start. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model} Reason={Reason}", diagnosticId, resolvedProvider, resolvedModel, FailureReason(ex, ct));
        }

        bool primarySucceeded = false;
        if (enumerator != null)
        {
            while (true)
            {
                string chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    chunk = enumerator.Current;
                }
                catch (Exception ex) when (ex is HttpRequestException
                                        || ex is TimeoutException
                                        || ex is TaskCanceledException)
                {
                    _logger.LogWarning(ex, "LLM stream provider failed. DiagnosticId={DiagnosticId} Provider={Provider} Model={Model} Reason={Reason}", diagnosticId, resolvedProvider, resolvedModel, FailureReason(ex, ct));
                    break;
                }
                primarySucceeded = true;
                yield return chunk;
            }
            await enumerator.DisposeAsync();
        }

        if (!primarySucceeded)
        {
            if (_fallbackChain.Enabled)
            {
                var fallbacks = await _fallbackChain.GetViableFallbacksAsync(ct);
                bool fallbackSuccess = false;

                foreach (var (fbProvider, fbModel) in fallbacks)
                {
                    var fbClient = _factory.Create(fbProvider);
                    IAsyncEnumerator<string>? fbEnumerator = null;
                    try
                    {
                        fbEnumerator = fbClient.StreamAsync(prompt, fbModel, ct).GetAsyncEnumerator(ct);
                    }
                    catch (Exception fbEx) when (fbEx is HttpRequestException
                                              || fbEx is TimeoutException
                                              || fbEx is TaskCanceledException)
                    {
                        continue;
                    }

                    if (fbEnumerator != null)
                    {
                        resolvedModel     = fbModel;
                        resolvedProvider  = fbProvider.ToString();
                        switchNote        = $"Switched to {fbProvider} ({fbModel}) — {sessionProvider} unavailable";
                        tierDowngradeNote = _fallbackChain.FallbackNote;
                        context.Metadata["tier_downgrade_note"] = tierDowngradeNote;

                        while (true)
                        {
                            string chunk;
                            try
                            {
                                if (!await fbEnumerator.MoveNextAsync())
                                    break;
                                chunk = fbEnumerator.Current;
                            }
                            catch (Exception fbEx) when (fbEx is HttpRequestException
                                                      || fbEx is TimeoutException
                                                      || fbEx is TaskCanceledException)
                            {
                                break;
                            }
                            fallbackSuccess = true;
                            yield return chunk;
                        }
                        await fbEnumerator.DisposeAsync();
                    }

                    if (fallbackSuccess)
                        break;
                }

                if (!fallbackSuccess)
                {
                    _logger.LogError("LLM stream providers exhausted. DiagnosticId={DiagnosticId} Reason={Reason}", diagnosticId, "all_attempts_failed");
                    yield return UnavailableMessage(diagnosticId);
                }
            }
            else
            {
                var fallback = _capacityRouter.SelectModel(TaskComplexity.Standard);
                var fallbackProvider = Enum.TryParse<LlmProvider>(fallback.ModelId.Provider, ignoreCase: true, out var fp)
                                               ? fp
                                               : _factory.DefaultProvider;

                client            = _factory.Create(fallbackProvider);
                resolvedModel     = fallback.ModelId.Model;
                resolvedProvider  = fallback.ModelId.Provider;
                switchNote        = $"Switched to {fallback.ModelId.Provider} ({fallback.ModelId.Model}) — {sessionProvider} unavailable";
                tierDowngradeNote = fallback.TierDowngradeNote;

                if (tierDowngradeNote is not null)
                    context.Metadata["tier_downgrade_note"] = tierDowngradeNote;

                var fbEnumerator = client.StreamAsync(prompt, resolvedModel, ct).GetAsyncEnumerator(ct);
                try
                {
                    while (await fbEnumerator.MoveNextAsync())
                    {
                        yield return fbEnumerator.Current;
                    }
                }
                finally
                {
                    await fbEnumerator.DisposeAsync();
                }
            }
        }
    }

    public async Task<string> WeaveAsync( ConversationContext    context
                                        , string                 originalResponse
                                        , IReadOnlyList<Insight> insights
                                        , CancellationToken      cancellationToken = default )
    {
        var prompt = BuildWeavePrompt(originalResponse, insights);

        _promptLogger.Log("WeaveLlmRouter.WeaveAsync", prompt, _defaults.ToString());

        return (await SendAsync(prompt, context, TaskComplexity.Light, cancellationToken)).Content;
    }

    private static string BuildWeavePrompt( string                 originalResponse
                                          , IReadOnlyList<Insight> insights )
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

    private static string UnavailableMessage(string diagnosticId) =>
            $"All configured LLM provider attempts failed. Diagnostic ID: {diagnosticId}. Check the Logs page for provider-specific details.";

    private static string FailureReason(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is TaskCanceledException && cancellationToken.IsCancellationRequested) return "cancelled";
        if (exception is TimeoutException or TaskCanceledException) return "timeout";
        if (exception is HttpRequestException httpException && httpException.StatusCode == System.Net.HttpStatusCode.TooManyRequests) return "rate_limited";
        if (exception is HttpRequestException) return "transport";
        return "internal";
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
