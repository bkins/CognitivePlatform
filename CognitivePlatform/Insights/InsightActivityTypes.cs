namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Stable <see cref="Domains.Activity.ActivityEvent.ActivityType"/> values logged by
/// the Insight Engine. One event per occurrence; consumed by analytics and (future)
/// coaching layers.
/// </summary>
public static class InsightActivityTypes
{
    public const string Emitted        = "insight.emitted";
    public const string Deduplicated   = "insight.deduplicated";
    public const string ProviderFailed = "insight.provider_failed";
    public const string WeaveFailed    = "insight.weave_failed";
}
