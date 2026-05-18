namespace CognitivePlatform.Api.Insights.Models;

public sealed record WhyInsightResult
(
    string                           InsightKey
  , string                           Explanation
  , IReadOnlyList<EvidenceReference> Evidence
);
