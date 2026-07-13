using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Singleton implementation of <see cref="ILlmCapacityRouter"/>.
/// Holds an in-process list of <see cref="LlmModelCapacity"/> entries ordered
/// by <see cref="LlmModelConfig.Priority"/> (lower value = preferred).
///
/// All mutable state is guarded by a single lock — concurrent requests are
/// expected to be infrequent relative to the window length.
/// </summary>
public class LlmCapacityRouter : ILlmCapacityRouter
{
    private readonly List<LlmModelCapacity> _models;
    private readonly ILlmRateLimiter        _rateLimiter;
    private readonly object                 _lock = new();

    public LlmCapacityRouter( IEnumerable<LlmModelConfig> configs
                             , ILlmRateLimiter             rateLimiter )
    {
        _rateLimiter = rateLimiter;

        _models = configs.OrderBy(config => config.Priority)
                         .Select(config => new LlmModelCapacity
                                           {
                                                   ModelId = config.Id
                                                 , Limits  = config.Limits
                                                 , Tier    = config.Tier
                                                 , Usage   = new LlmUsageState()
                                           })
                         .ToList();
    }

    /// <inheritdoc/>
    public LlmModelId SelectModel() => SelectModel(TaskComplexity.Standard).ModelId;

    /// <inheritdoc/>
    public LlmModelCapacity SelectModel(TaskComplexity complexity)
    {
        lock (_lock)
        {
            if (_models.Count == 0) throw new LlmCapacityExceededException("No LLM models are configured.");

            foreach (var capacity in _models) RollWindowIfExpired(capacity);

            // First pass: highest-priority non-exhausted model at the ideal tier.
            foreach (var capacity in _models.Where(capacity => capacity.IsExhausted.Not() 
                                                            && _rateLimiter.IsExhausted(capacity.ModelId.Provider).Not())
                                            .Where(capacity => IsIdealTierFor(capacity.Tier, complexity)))
            {
                return capacity;
            }

            // Second pass: acceptable fallback tier (Heavy→Standard downgrade, or Standard→Heavy upgrade).
            foreach (var capacity in _models)
            {
                if (capacity.IsExhausted 
                 || _rateLimiter.IsExhausted(capacity.ModelId.Provider))
                    continue;

                if (IsAcceptableFallbackFor(capacity.Tier, complexity).Not()) continue;
                
                var note = BuildDowngradeNote(capacity.Tier, complexity);
                
                return note is null
                               ? capacity
                               : new LlmModelCapacity
                                 {
                                         ModelId           = capacity.ModelId
                                       , Limits            = capacity.Limits
                                       , Tier              = capacity.Tier
                                       , Usage             = capacity.Usage
                                       , TierDowngradeNote = note
                                 };
            }

            throw new LlmCapacityExceededException();
        }
    }

    /// <inheritdoc/>
    public void RecordUsage(LlmModelId modelId, LlmUsageInfo usage)
    {
        lock (_lock)
        {
            var capacity = FindCapacity(modelId);
            if (capacity is null) return;

            RollWindowIfExpired(capacity);

            capacity.Usage.RequestsUsed += 1;
            capacity.Usage.TokensUsed   += usage.TotalTokens;
        }
    }

    /// <inheritdoc/>
    public void RecordRateLimits(LlmModelId modelId, LlmRateLimitSnapshot snapshot)
    {
        var metadata = new LlmResponseMetadata
                       {
                               ProviderId  = modelId.Provider
                             , ModelId     = modelId.Model
                             , RateLimits  = snapshot
                             , CapturedUtc = DateTimeOffset.UtcNow
                       };

        _rateLimiter.Update(metadata);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static bool IsIdealTierFor( TaskComplexity modelTier
                                      , TaskComplexity requestedComplexity )
        => requestedComplexity switch
        {
                TaskComplexity.Heavy    => modelTier == TaskComplexity.Heavy
              , TaskComplexity.Standard => modelTier == TaskComplexity.Standard
              , TaskComplexity.Light    => true
              , _                       => true
        };

    private static bool IsAcceptableFallbackFor( TaskComplexity modelTier
                                               , TaskComplexity requestedComplexity )
    {
        return requestedComplexity switch
        {
                TaskComplexity.Heavy    => modelTier == TaskComplexity.Standard
              , TaskComplexity.Standard => modelTier == TaskComplexity.Heavy
              , _                       => false
        };
    }

    private static string? BuildDowngradeNote(TaskComplexity modelTier, TaskComplexity requestedComplexity)
    {
        if (requestedComplexity == TaskComplexity.Heavy 
         && modelTier == TaskComplexity.Standard)
        {
            return "Note: the model best suited for this analysis isn't available right now. "
                 + "Results may be less detailed than usual — try again later for the best output.";
        }

        return null;
    }

    private LlmModelCapacity? FindCapacity(LlmModelId modelId)
    {
        return _models.FirstOrDefault(capacity => string.Equals(capacity.ModelId.Provider
                                                              , modelId.Provider
                                                              , StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(capacity.ModelId.Model
                                                              , modelId.Model
                                                              , StringComparison.OrdinalIgnoreCase));
    }

    private static void RollWindowIfExpired(LlmModelCapacity capacity)
    {
        if (capacity.Limits.Window is null) return;

        var windowEnd = capacity.Usage.WindowStart + capacity.Limits.Window.Value;

        if (DateTimeOffset.UtcNow < windowEnd) return;

        capacity.Usage.RequestsUsed = 0;
        capacity.Usage.TokensUsed   = 0;
        capacity.Usage.WindowStart  = DateTimeOffset.UtcNow;
    }
}
