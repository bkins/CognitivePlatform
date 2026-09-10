namespace CognitivePlatform.Api.Domains.MemoryReview;

public interface IMemoryReviewService
{
    Task<MemoryReviewResult> GetReviewAsync(CancellationToken cancellationToken = default);
}
