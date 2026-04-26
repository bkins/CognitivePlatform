namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Phase A history entry: one row per emitted insight, keyed on
/// <see cref="DeduplicationKey"/>. Stored in process memory only; persistence and
/// outcome tracking (ActedOn / Dismissed / Expired) are deferred to Phase B / C.
/// </summary>
public sealed record InsightHistoryItem(
        string          DeduplicationKey
      , DateTime        EmittedAt
      , InsightCategory Category
      , string?         Message);
