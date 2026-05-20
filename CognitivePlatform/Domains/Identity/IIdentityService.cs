namespace CognitivePlatform.Api.Domains.Identity;

public interface IIdentityService
{
    Task<PersonProfile>                    GetProfileAsync      (CancellationToken ct);
    Task                                   UpdateProfileAsync   (PersonProfile profile, CancellationToken ct);
    Task<IReadOnlyList<IdentityAssertion>> GetAssertionsAsync   (CancellationToken ct);
    Task                                   AddAssertionAsync    (IdentityAssertion assertion, CancellationToken ct);
    Task                                   ConfirmAssertionAsync(string assertionId, CancellationToken ct);
}
