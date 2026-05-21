namespace CognitivePlatform.Api.Domains.Identity;

public interface IIdentityAnalysisService
{
    Task<IReadOnlyList<DerivedInsight>> GenerateInsightsAsync  (string partitionKey, CancellationToken ct);
    Task<PersonalitySnapshot>           GenerateSnapshotAsync  (string partitionKey, CancellationToken ct);

    Task<string> ReflectOnChangeAsync  (string partitionKey, TimeSpan window,                  CancellationToken ct);
    Task<string> IdentifyPatternsAsync (string partitionKey,                                    CancellationToken ct);
    Task<string> AnalyzeStressorsAsync (string partitionKey,                                    CancellationToken ct);
    Task<string> SummarizeGrowthAsync  (string partitionKey,                                    CancellationToken ct);
    Task<string> CompareSnapshotsAsync (string partitionKey, DateTime from, DateTime to,        CancellationToken ct);
}
