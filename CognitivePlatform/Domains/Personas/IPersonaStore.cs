using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaStore
{
    Task<CanonicalPersona?>              GetAsync(Guid personaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CanonicalPersona>> GetAllAsync(CancellationToken cancellationToken = default);
    Task                                 SaveAsync(CanonicalPersona persona, CancellationToken cancellationToken = default);
    Task                                 SoftDeleteAsync(Guid personaId, CancellationToken cancellationToken = default);

    Task                                 AddMemoryAsync(PersonaMemory memory, CancellationToken cancellationToken = default);
    Task                                 UpdateMemoryStateAsync(Guid memoryId, Guid personaId, MemoryState newState, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonaMemory>>   GetMemoriesAsync(Guid personaId, CancellationToken cancellationToken = default);

    Task                                 SaveSnapshotAsync(MemorySnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemorySnapshot>>  GetSnapshotsAsync(Guid personaId, CancellationToken cancellationToken = default);
}
