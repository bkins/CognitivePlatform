namespace CognitivePlatform.Api.Domains.MemoryReview;

public sealed record MemoryReviewItem(
    MemoryReviewSourceKind SourceKind,
    string SourceId,
    string SourceLabel,
    MemoryReviewState State,
    string Content,
    double? Confidence,
    DateTimeOffset LastReinforcedOrModifiedUtc,
    IReadOnlyList<string> Provenance,
    IReadOnlyList<string> AvailableOperations);
