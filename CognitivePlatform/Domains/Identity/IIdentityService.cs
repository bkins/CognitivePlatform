namespace CognitivePlatform.Api.Domains.Identity;

public interface IIdentityService
{
    Task<PersonProfile>                    GetProfileAsync       (CancellationToken ct);
    Task                                   UpdateProfileAsync    (PersonProfile profile, CancellationToken ct);
    Task<IReadOnlyList<IdentityAssertion>> GetAssertionsAsync    (CancellationToken ct);
    Task                                   AddAssertionAsync     (IdentityAssertion assertion, CancellationToken ct);
    Task                                   ConfirmAssertionAsync (string assertionId, CancellationToken ct);

    Task<IReadOnlyList<DerivedInsight>>      GetDerivedInsightsAsync   (CancellationToken ct);
    Task                                     AddDerivedInsightAsync    (DerivedInsight insight, CancellationToken ct);
    Task                                     ConfirmDerivedInsightAsync(string insightId, CancellationToken ct);
    Task<IReadOnlyList<PersonalitySnapshot>> GetSnapshotsAsync         (CancellationToken ct);
    Task                                     AddSnapshotAsync          (PersonalitySnapshot snapshot, CancellationToken ct);
    Task<PersonalitySnapshot?>               GetLatestSnapshotAsync    (CancellationToken ct);
}
