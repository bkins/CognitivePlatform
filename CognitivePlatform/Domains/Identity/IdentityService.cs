using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Workspace;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Identity;

public class IdentityService : IIdentityService
{
    private const string ProfileIdPrefix = "person-profile";

    private readonly IObjectStore      _store;
    private readonly IWorkspaceContext _workspaceContext;

    public IdentityService(IObjectStore store, IWorkspaceContext workspaceContext)
    {
        _store            = store;
        _workspaceContext = workspaceContext;
    }

    // Workspace-scoped so two workspaces never share a row in the Id-keyed store.
    public static string GetProfileId(string? partitionKey)
        => $"{ProfileIdPrefix}-{partitionKey ?? "personal"}";

    private string ProfileId => GetProfileId(_workspaceContext.ActivePartitionKey);

    public async Task<PersonProfile> GetProfileAsync(CancellationToken ct)
    {
        var existing = _store.Get<PersonProfile>(ProfileId
                                               , partitionKey: _workspaceContext.ActivePartitionKey);

        if (existing is not null)
            return existing;

        var empty = new PersonProfile { Id = ProfileId };

        await _store.Save(empty
                        , partitionKey: _workspaceContext.ActivePartitionKey
                        , id:           ProfileId);

        return empty;
    }

    public async Task UpdateProfileAsync(PersonProfile profile, CancellationToken ct)
    {
        await _store.Save(profile
                        , partitionKey: _workspaceContext.ActivePartitionKey
                        , id:           ProfileId);
    }

    public Task<IReadOnlyList<IdentityAssertion>> GetAssertionsAsync(CancellationToken ct)
    {
        IReadOnlyList<IdentityAssertion> assertions =
            _store.List<IdentityAssertion>(partitionKey: _workspaceContext.ActivePartitionKey)
                  .Where(assertion => assertion.IsDeleted.Not())
                  .OrderBy(assertion => assertion.Topic)
                  .ThenByDescending(assertion => assertion.LastReinforced)
                  .ToList();

        return Task.FromResult(assertions);
    }

    public async Task AddAssertionAsync(IdentityAssertion assertion, CancellationToken ct)
    {
        await _store.Save(assertion
                        , partitionKey: _workspaceContext.ActivePartitionKey
                        , id:           assertion.Id);
    }

    public async Task ConfirmAssertionAsync(string assertionId, CancellationToken ct)
    {
        var existing = _store.Get<IdentityAssertion>(assertionId
                                                   , partitionKey: _workspaceContext.ActivePartitionKey);

        if (existing is null || existing.IsDeleted)
            return;

        var confirmed = existing with
                        {
                                UserConfirmed  = true
                              , LastReinforced = DateTime.UtcNow
                        };

        await _store.Save(confirmed
                        , partitionKey: _workspaceContext.ActivePartitionKey
                        , id:           assertionId);
    }
}
