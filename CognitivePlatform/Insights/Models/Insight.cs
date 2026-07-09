using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// A read-only observation surfaced by the Insight Engine.
/// Insights are woven into the natural language response — they suggest, they do not execute.
///
/// Immutable record. Equality is structural, but in practice insights are correlated by
/// <see cref="DeduplicationKey"/> rather than full-record equality (the optional
/// <see cref="SuggestedParameters"/> dictionary makes structural equality reference-based).
/// </summary>
public sealed record Insight
{
    /// <summary>Human-facing suggestion, woven into the LLM response.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional: name of an NLCS-registered action this insight is nudging toward.
    /// Must match a name in IActionRegistry or the insight is suppressed.
    /// </summary>
    public string? SuggestedAction { get; init; }

    /// <summary>
    /// Optional pre-filled parameters for the suggested action.
    /// Keys must match ParameterMetadata.Name entries on the target action.
    /// </summary>
    public Dictionary<string, string>? SuggestedParameters { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InsightPriority Priority { get; init; } = InsightPriority.Normal;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InsightCategory Category { get; init; } = InsightCategory.General;

    /// <summary>
    /// Stable key used for deduplication across turns and sessions.
    /// Format: "{domain}.{specific-signal}"
    /// </summary>
    public string DeduplicationKey { get; init; } = string.Empty;

    /// <summary>Optional reasoning metadata, surfaced by WhyInsight.</summary>
    public InsightReasoning? Reasoning { get; init; }
}
