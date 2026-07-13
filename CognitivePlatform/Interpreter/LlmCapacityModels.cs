namespace CognitivePlatform.Api.Interpreter;

// -----------------------------------------------------------------------
// EPIC-07 Phase B — capacity-aware multi-provider/multi-model routing
// -----------------------------------------------------------------------

/// <summary>
/// Identifies an LLM model by provider and model name.
/// </summary>
public record LlmModelId(string Provider, string Model);

/// <summary>
/// Static rate-limit configuration for a model or provider window.
/// All fields are optional; a null value means "no configured limit".
/// </summary>
public class LlmRateLimits
{
    public int?      RequestsPerWindow { get; init; }
    public int?      TokensPerWindow   { get; init; }
    public TimeSpan? Window            { get; init; }
}

/// <summary>
/// Mutable runtime usage state tracked locally per model within the current window.
/// </summary>
public class LlmUsageState
{
    public int            RequestsUsed { get; set; }
    public int            TokensUsed   { get; set; }
    public DateTimeOffset WindowStart  { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Combined capacity snapshot for a single model — limits plus current usage.
/// <see cref="IsExhausted"/> drives routing decisions in <see cref="ILlmCapacityRouter"/>.
/// </summary>
public class LlmModelCapacity
{
    public LlmModelId     ModelId { get; init; } = default!;
    public LlmRateLimits  Limits  { get; init; } = new();
    public LlmUsageState  Usage   { get; init; } = new();
    public TaskComplexity Tier    { get; init; } = TaskComplexity.Standard;

    /// <summary>
    /// Set when the selected model's Tier is below the requested TaskComplexity.
    /// Null when the model meets or exceeds the requested tier.
    /// </summary>
    public string? TierDowngradeNote { get; init; }

    public int RemainingRequests => Limits.RequestsPerWindow.HasValue
                                            ? Limits.RequestsPerWindow.Value - Usage.RequestsUsed
                                            : int.MaxValue;

    public int RemainingTokens => Limits.TokensPerWindow.HasValue
                                          ? Limits.TokensPerWindow.Value - Usage.TokensUsed
                                          : int.MaxValue;

    public bool IsExhausted => RemainingRequests <= 0 || RemainingTokens <= 0;
}

/// <summary>
/// Static configuration for a model entry in the capacity registry.
/// Bound from the "LlmModels" section of appsettings.
/// </summary>
public class LlmModelConfig
{
    public string        Provider { get; init; } = string.Empty;
    public string        Model    { get; init; } = string.Empty;
    public int           Priority { get; init; }
    public LlmRateLimits Limits   { get; init; } = new();

    /// <summary>
    /// The complexity tier this model is preferred for.
    /// Used by ILlmCapacityRouter.SelectModel(TaskComplexity) to prefer appropriate models.
    /// Defaults to Standard if not configured.
    /// </summary>
    public TaskComplexity Tier { get; init; } = TaskComplexity.Standard;

    public LlmModelId Id => new(Provider, Model);
}

/// <summary>
/// Thrown by <see cref="ILlmCapacityRouter.SelectModel"/> when every configured
/// provider and model has been exhausted for the current window.
/// </summary>
public class LlmCapacityExceededException : Exception
{
    public LlmCapacityExceededException()
        : base("All configured LLM providers and models are exhausted.") { }

    public LlmCapacityExceededException(string message) : base(message) { }
}
