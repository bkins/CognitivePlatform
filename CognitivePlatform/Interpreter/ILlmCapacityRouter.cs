namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Capacity-aware model selector. Maintains per-model usage state and routing
/// priority to ensure requests are directed to a non-exhausted model.
///
/// Implementations must be singleton; rate-limit and usage state is process-wide.
///
/// Routing rules (in priority order):
///   1. Return the highest-priority model that is not exhausted.
///   2. Switch to the next available model within the same provider.
///   3. Switch to the lowest-priority provider that has a non-exhausted model.
///   4. Throw <see cref="LlmCapacityExceededException"/> when nothing is available.
/// </summary>
public interface ILlmCapacityRouter
{
    /// <summary>
    /// Returns the best available model according to priority and capacity state.
    /// Delegates to <see cref="SelectModel(TaskComplexity)"/> with Standard complexity.
    /// </summary>
    /// <exception cref="LlmCapacityExceededException">
    /// All configured models are exhausted or no models are configured.
    /// </exception>
    LlmModelId SelectModel();

    /// <summary>
    /// Returns the best available model for the requested <paramref name="complexity"/> tier.
    /// Prefers models whose configured <see cref="LlmModelConfig.Tier"/> matches the request,
    /// falling back to a lower-tier model and setting <see cref="LlmModelCapacity.TierDowngradeNote"/>
    /// when the ideal tier is unavailable.
    /// </summary>
    /// <exception cref="LlmCapacityExceededException">
    /// All models at an acceptable tier are exhausted or no models are configured.
    /// </exception>
    LlmModelCapacity SelectModel(TaskComplexity complexity);

    /// <summary>
    /// Increments usage counters for <paramref name="modelId"/> and rolls the
    /// tracking window over if it has expired.
    /// </summary>
    void RecordUsage(LlmModelId modelId, LlmUsageInfo usage);

    /// <summary>
    /// Persists the provider rate-limit snapshot to <see cref="ILlmRateLimiter"/>
    /// so that header-based signals are visible to the wider pipeline.
    /// </summary>
    void RecordRateLimits(LlmModelId modelId, LlmRateLimitSnapshot snapshot);
}
