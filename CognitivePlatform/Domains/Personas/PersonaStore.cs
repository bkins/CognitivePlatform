using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaStore : IPersonaStore
{
    private readonly IObjectStore _store;

    public PersonaStore(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<CanonicalPersona?> GetAsync(Guid personaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var persona = _store.Get<CanonicalPersona>(personaId.ToString()
                                                 , partitionKey: personaId.ToString());

        if (persona is null || persona.IsDeleted)
            return Task.FromResult<CanonicalPersona?>(null);

        return Task.FromResult<CanonicalPersona?>(persona);
    }

    public Task<IReadOnlyList<CanonicalPersona>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<CanonicalPersona> personas = _store
            .List<CanonicalPersona>()
            .Where(persona => !persona.IsDeleted)
            .OrderBy(persona => persona.Name)
            .ToList();

        return Task.FromResult(personas);
    }

    public async Task SaveAsync(CanonicalPersona persona, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        persona.LastModifiedUtc = DateTime.UtcNow;

        await _store.Save(persona
                        , partitionKey: persona.Id.ToString()
                        , id:           persona.Id.ToString());
    }

    public async Task SoftDeleteAsync(Guid personaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = _store.Get<CanonicalPersona>(personaId.ToString()
                                                  , partitionKey: personaId.ToString());

        if (existing is null || existing.IsDeleted)
            return;

        existing.IsDeleted       = true;
        existing.DeletedUtc      = DateTime.UtcNow;
        existing.LastModifiedUtc = DateTime.UtcNow;

        await _store.Save(existing
                        , partitionKey: personaId.ToString()
                        , id:           personaId.ToString());
    }

    public async Task AddMemoryAsync(PersonaMemory memory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _store.Save(memory
                        , partitionKey: $"memory:{memory.PersonaId}"
                        , id:           memory.Id.ToString());
    }

    public async Task UpdateMemoryStateAsync( Guid              memoryId
                                            , Guid              personaId
                                            , MemoryState       newState
                                            , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = _store.Get<PersonaMemory>(memoryId.ToString()
                                               , partitionKey: $"memory:{personaId}");

        if (existing is null)
            return;

        existing.State           = newState;
        existing.LastModifiedUtc = DateTime.UtcNow;

        await _store.Save(existing
                        , partitionKey: $"memory:{personaId}"
                        , id:           memoryId.ToString());
    }

    public Task<IReadOnlyList<PersonaMemory>> GetMemoriesAsync(Guid personaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<PersonaMemory> memories = _store
            .List<PersonaMemory>(partitionKey: $"memory:{personaId}")
            .OrderByDescending(memory => memory.CreatedUtc)
            .ToList();

        return Task.FromResult(memories);
    }

    public async Task SaveSnapshotAsync(MemorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _store.Save(snapshot
                        , partitionKey: $"snapshot:{snapshot.PersonaId}"
                        , id:           snapshot.Id.ToString());
    }

    public Task<IReadOnlyList<MemorySnapshot>> GetSnapshotsAsync(Guid personaId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MemorySnapshot> snapshots = _store
            .List<MemorySnapshot>(partitionKey: $"snapshot:{personaId}")
            .OrderByDescending(snapshot => snapshot.CreatedUtc)
            .ToList();

        return Task.FromResult(snapshots);
    }
}
