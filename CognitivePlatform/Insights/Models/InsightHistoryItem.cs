namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Persisted record of an emitted insight. Used for cross-session deduplication,
/// throttling, and coaching analytics. Stub implementation in Phase A.
/// </summary>
public sealed class InsightHistoryItem
{
    public string          Id           { get; init; } = Guid.NewGuid().ToString();
    public string          InsightKey   { get; init; } = string.Empty;
    public InsightCategory Category     { get; init; }
    public DateTime        EmittedAtUtc { get; init; }
    public InsightOutcome  Outcome      { get; set; }  = InsightOutcome.Unknown;
    public DateTime?       ResolvedAtUtc { get; set; }
}
