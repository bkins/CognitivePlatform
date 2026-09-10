namespace CognitivePlatform.Api.Domains.MemoryReview;

public sealed record MemoryReviewResult(
    IReadOnlyList<MemoryReviewItem> Items,
    IReadOnlyList<MemoryReviewSourceStatus> Sources);
