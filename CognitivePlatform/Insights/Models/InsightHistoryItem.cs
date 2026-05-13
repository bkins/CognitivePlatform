using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// One row per emitted insight. Phase A stored this in process memory only;
/// Phase B persists it to the Object Store for cross-session deduplication,
/// throttling, and outcome tracking.
///
/// <para>
/// <see cref="DeduplicationKey"/> and <see cref="EmittedAt"/> are backward-compat
/// aliases for <see cref="InsightKey"/> and <see cref="EmittedAtUtc"/> respectively,
/// so existing in-memory store and test code continues to compile without change.
/// </para>
/// </summary>
public sealed class InsightHistoryItem
{
    public string          Id            { get; set; } = Guid.NewGuid().ToString();
    public string          InsightKey    { get; set; } = string.Empty;
    public InsightCategory Category      { get; set; }
    public DateTime        EmittedAtUtc  { get; set; }
    public string?         Message       { get; set; }
    public InsightOutcome  Outcome       { get; set; } = InsightOutcome.Unknown;
    public DateTime?       ResolvedAtUtc { get; set; }
    public bool            IsDeleted     { get; set; }
    public DateTime?       DeletedUtc    { get; set; }

    /// <summary>Alias for <see cref="InsightKey"/> — kept for backward compatibility.</summary>
    [JsonIgnore]
    public string DeduplicationKey => InsightKey;

    /// <summary>Alias for <see cref="EmittedAtUtc"/> — kept for backward compatibility.</summary>
    [JsonIgnore]
    public DateTime EmittedAt => EmittedAtUtc;
}
