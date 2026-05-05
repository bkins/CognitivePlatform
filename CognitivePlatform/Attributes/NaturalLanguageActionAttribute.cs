namespace CognitivePlatform.Api.Attributes;

[AttributeUsage(AttributeTargets.Method
              , AllowMultiple = false)]
public class NaturalLanguageActionAttribute : Attribute
{
    /// <summary>
    /// A human-friendly description of what this action does.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Example natural language phrases that invoke this action.
    /// Used by interpreters for matching and intent shaping.
    /// </summary>
    public string[]? Examples { get; init; }

    /// <summary>
    /// Optional category for grouping and meta-reasoning.
    /// Normalized internally and defaults to "General".
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// If true, the orchestrator may ask the user a follow-up question
    /// when required parameters are missing, instead of just failing.
    /// </summary>
    public bool AllowsClarification { get; init; } = false;

    /// <summary>
    /// If true, the offline intent queue may re-execute this action after a reconnect.
    /// Only set for idempotent, state-mutating actions where replay is safe.
    /// </summary>
    public bool IsReplayable { get; init; } = false;

}