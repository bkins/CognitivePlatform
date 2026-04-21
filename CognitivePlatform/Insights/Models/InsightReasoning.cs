namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Lightweight metadata attached to an <see cref="Insight"/> so the system can explain
/// why it was generated. Not shown to the user by default; surfaced by WhyInsight.
/// </summary>
public sealed class InsightReasoning
{
    public string                          Explanation { get; init; } = string.Empty;
    public IReadOnlyList<EvidenceReference> Evidence   { get; init; } = [];
}
