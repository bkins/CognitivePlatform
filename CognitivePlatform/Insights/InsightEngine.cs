using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Registry;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Insights;

public sealed class InsightEngine : IInsightEngine
{
    private readonly IEnumerable<IInsightProvider> _providers;
    private readonly IActionRegistry               _registry;
    private readonly IInsightHistoryStore          _historyStore;
    private readonly IObjectStore                  _store;
    private readonly InsightPolicy                 _policy;
    private readonly ILogger<InsightEngine>        _logger;

    public InsightEngine( IEnumerable<IInsightProvider> providers
                        , IActionRegistry               registry
                        , IInsightHistoryStore          historyStore
                        , IObjectStore                  store
                        , InsightPolicy                 policy
                        , ILogger<InsightEngine>        logger )
    {
        _providers    = providers    ?? throw new ArgumentNullException(nameof(providers));
        _registry     = registry     ?? throw new ArgumentNullException(nameof(registry));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _store        = store        ?? throw new ArgumentNullException(nameof(store));
        _policy       = policy       ?? throw new ArgumentNullException(nameof(policy));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Insight>> GenerateInsightsAsync( ConversationContext context
                                                                    , CancellationToken   ct = default )
    {
        var providerTasks = _providers
            .Select(provider => CollectFromProviderAsync(provider, context, ct))
            .ToList();

        var results = await Task.WhenAll(providerTasks);

        var allInsights = results.SelectMany(insightList => insightList).ToList();

        return await RankAndFilterAsync(allInsights, ct);
    }

    private async Task<List<Insight>> CollectFromProviderAsync( IInsightProvider   provider
                                                               , ConversationContext context
                                                               , CancellationToken  ct )
    {
        var collected = new List<Insight>();

        try
        {
            await foreach (var insight in provider.GenerateAsync(context, _store, ct))
                collected.Add(insight);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex
                             , "Insight provider {Category} faulted and was skipped"
                             , provider.Category);
        }

        return collected;
    }

    private async Task<IReadOnlyList<Insight>> RankAndFilterAsync( List<Insight>     insights
                                                                  , CancellationToken ct )
    {
        var validated = ValidateActions(insights).ToList();

        var deduped = new List<Insight>();
        foreach (var insight in validated)
        {
            var window    = _policy.GetRepeatWindow(insight.Category);
            var wasRecent = await _historyStore.WasRecentlyEmittedAsync(insight.DeduplicationKey
                                                                       , window
                                                                       , ct);
            if (!wasRecent) deduped.Add(insight);
        }

        return deduped
            .GroupBy(insight => insight.DeduplicationKey)
            .Select(group => group.First())
            .OrderByDescending(insight => insight.Priority)
            .Take(_policy.MaxPerTurn)
            .ToList();
    }

    /// <summary>
    /// Any insight with a SuggestedAction that does not exist in the registry is suppressed.
    /// </summary>
    private IEnumerable<Insight> ValidateActions(IEnumerable<Insight> insights)
    {
        foreach (var insight in insights)
        {
            if (insight.SuggestedAction is null)
            {
                yield return insight;
                continue;
            }

            if (_registry.FindByName(insight.SuggestedAction) is not null)
            {
                yield return insight;
            }
            else
            {
                _logger.LogWarning( "Insight suppressed: SuggestedAction '{ActionName}' is not registered. "
                                  + "DeduplicationKey={Key}"
                                  , insight.SuggestedAction
                                  , insight.DeduplicationKey);
            }
        }
    }
}
