namespace CognitivePlatform.Api.Domains.Identity;

public interface IIdentityAnalysisService
{
    Task<IReadOnlyList<DerivedInsight>> GenerateInsightsAsync (string partitionKey, CancellationToken ct);
    Task<PersonalitySnapshot>           GenerateSnapshotAsync (string partitionKey, CancellationToken ct);
}
