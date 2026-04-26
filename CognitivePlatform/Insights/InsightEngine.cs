using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Registry;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase A engine. Fans out across registered providers concurrently, applies
/// <see cref="InsightPolicy"/> (per-turn cap, cross-session dedup, action-name
/// validation), records survivors, and emits one <see cref="IActivityLog"/> event
/// per lifecycle outcome (emitted / deduplicated / provider_failed).
///
/// Failure isolation: a faulted provider is logged and skipped — it does not
/// break the turn. The orchestrator owns weave-failure handling separately.
/// </summary>
public sealed class InsightEngine : IInsightEngine
{
    private readonly IEnumerable<IInsightProvider> _providers;
    private readonly IActionRegistry               _registry;
    private readonly IInsightHistoryStore          _historyStore;
    private readonly IActivityLog                  _activityLog;
    private readonly InsightPolicy                 _policy;
    private readonly ILogger<InsightEngine>        _logger;

    public InsightEngine( IEnumerable<IInsightProvider> providers
                        , IActionRegistry               registry
                        , IInsightHistoryStore          historyStore
                        , IActivityLog                  activityLog
                        , InsightPolicy                 policy
                        , ILogger<InsightEngine>        logger )
    {
        _providers    = providers    ?? throw new ArgumentNullException(nameof(providers));
        _registry     = registry     ?? throw new ArgumentNullException(nameof(registry));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _activityLog  = activityLog  ?? throw new ArgumentNullException(nameof(activityLog));
        _policy       = policy       ?? throw new ArgumentNullException(nameof(policy));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Insight>> GenerateInsightsAsync(
        ConversationContext context
      , CancellationToken   cancellationToken = default )
    {
        var providerTasks = _providers
            .Select(provider => CollectFromProviderAsync(provider, context, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(providerTasks);

        var allInsights = results.SelectMany(insightList => insightList).ToList();

        return await RankAndFilterAsync(allInsights, cancellationToken);
    }

    private async Task<List<Insight>> CollectFromProviderAsync(
        IInsightProvider    provider
      , ConversationContext context
      , CancellationToken   cancellationToken )
    {
        var collected = new List<Insight>();

        try
        {
            await foreach (var insight in provider.GenerateAsync(context, cancellationToken))
                collected.Add(insight);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex
                             , "Insight provider {Category} faulted and was skipped"
                             , provider.Category);

            await LogActivityAsync(InsightActivityTypes.ProviderFailed
                                 , notes: $"{provider.Category}: {ex.GetType().Name}: {ex.Message}"
                                 , category: provider.Category
                                 , cancellationToken: cancellationToken);
        }

        return collected;
    }

    private async Task<IReadOnlyList<Insight>> RankAndFilterAsync(
        List<Insight>     insights
      , CancellationToken cancellationToken )
    {
        var validated = ValidateActions(insights).ToList();

        var deduped = new List<Insight>();
        foreach (var insight in validated)
        {
            var window    = _policy.GetRepeatWindow(insight.Category);
            var wasRecent = await _historyStore.WasRecentlyEmittedAsync(insight.DeduplicationKey
                                                                      , window
                                                                      , cancellationToken);

            if (wasRecent)
            {
                await LogActivityAsync(InsightActivityTypes.Deduplicated
                                     , notes: insight.DeduplicationKey
                                     , category: insight.Category
                                     , cancellationToken: cancellationToken);
                continue;
            }

            deduped.Add(insight);
        }

        var survivors = deduped
            .GroupBy(insight => insight.DeduplicationKey)
            .Select(group => group.First())
            .OrderByDescending(insight => insight.Priority)
            .Take(_policy.MaxPerTurn)
            .ToList();

        foreach (var insight in survivors)
        {
            await LogActivityAsync(InsightActivityTypes.Emitted
                                 , notes: insight.DeduplicationKey
                                 , category: insight.Category
                                 , cancellationToken: cancellationToken);
        }

        return survivors;
    }

    /// <summary>
    /// Any insight with a <see cref="Insight.SuggestedAction"/> that does not exist
    /// in the registry is suppressed.
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
                _logger.LogWarning("Insight suppressed: SuggestedAction '{ActionName}' is not registered. "
                                 + "DeduplicationKey={Key}"
                                 , insight.SuggestedAction
                                 , insight.DeduplicationKey);
            }
        }
    }

    private Task LogActivityAsync( string            activityType
                                 , string            notes
                                 , InsightCategory   category
                                 , CancellationToken cancellationToken )
    {
        var activityEvent = new ActivityEvent
                            {
                                    ActivityType = activityType
                                  , Notes        = notes
                                  , Meta         = new Dictionary<string, string>
                                                   {
                                                           ["category"] = category.ToString()
                                                   }
                            };

        return _activityLog.LogAsync(activityEvent, cancellationToken);
    }
}
