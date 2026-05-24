using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class MemorySnapshotService : IMemorySnapshotService
{
    private readonly IPersonaStore          _personaStore;
    private readonly IPersonaSessionManager _sessionManager;

    public MemorySnapshotService( IPersonaStore          personaStore
                                , IPersonaSessionManager sessionManager )
    {
        _personaStore   = personaStore   ?? throw new ArgumentNullException(nameof(personaStore));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<MemorySnapshot> CreateSnapshotAsync( Guid              personaId
                                                          , string            name
                                                          , string            notes
                                                          , CancellationToken cancellationToken = default )
    {
        var persona = await _personaStore.GetAsync(personaId, cancellationToken);

        if (persona is null)
            throw new InvalidOperationException($"Persona '{personaId}' not found.");

        var currentMemories = await _personaStore.GetMemoriesAsync(personaId, cancellationToken);

        var snapshot = new MemorySnapshot
                       {
                               Id               = Guid.NewGuid()
                             , PersonaId        = personaId
                             , Name             = name
                             , Notes            = notes ?? string.Empty
                             , Configuration    = persona.Configuration
                             , IncludedMemories = currentMemories
                                                      .Select(memory => memory.Id)
                                                      .ToList()
                             , CreatedUtc       = DateTime.UtcNow
                       };

        await _personaStore.SaveSnapshotAsync(snapshot, cancellationToken);

        return snapshot;
    }

    public async Task LoadSnapshotAsync( Guid              snapshotId
                                        , string            conversationId
                                        , CancellationToken cancellationToken = default )
    {
        var allPersonas = await _personaStore.GetAllAsync(cancellationToken);

        MemorySnapshot? snapshot = null;

        foreach (var candidate in allPersonas)
        {
            var snapshots = await _personaStore.GetSnapshotsAsync(candidate.Id, cancellationToken);
            snapshot = snapshots.FirstOrDefault(snapshotCandidate => snapshotCandidate.Id == snapshotId);

            if (snapshot is not null)
                break;
        }

        if (snapshot is null)
            throw new InvalidOperationException($"Snapshot '{snapshotId}' not found.");

        var personaMemories = await _personaStore.GetMemoriesAsync(snapshot.PersonaId, cancellationToken);

        var includedMemories = personaMemories
            .Where(memory => snapshot.IncludedMemories.Contains(memory.Id))
            .ToList();

        var snapshotOverride = new PersonaSnapshotOverride(
            snapshotId
          , snapshot.Configuration
          , includedMemories);

        _sessionManager.SetSnapshotOverride(conversationId, snapshotOverride);
    }

    public async Task<IReadOnlyList<MemorySnapshot>> GetSnapshotsAsync( Guid              personaId
                                                                       , CancellationToken cancellationToken = default )
    {
        return await _personaStore.GetSnapshotsAsync(personaId, cancellationToken);
    }

    public Task UnloadSnapshotAsync( string            conversationId
                                   , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _sessionManager.ClearSnapshotOverride(conversationId);

        return Task.CompletedTask;
    }
}
