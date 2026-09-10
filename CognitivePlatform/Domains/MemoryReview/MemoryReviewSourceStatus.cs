namespace CognitivePlatform.Api.Domains.MemoryReview;

public sealed record MemoryReviewSourceStatus(
    MemoryReviewSourceKind SourceKind,
    bool IsAvailable,
    string? Message = null);
