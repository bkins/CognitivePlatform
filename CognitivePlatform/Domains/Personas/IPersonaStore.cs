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

    /// <summary>
    /// Marks a memory as <see cref="MemoryState.Contradicted"/>, records the IDs of the
    /// memories it conflicts with, and stamps <c>LastModifiedUtc</c>.
    /// </summary>
    Task MarkMemoryContradictedAsync( Guid              memoryId
                                    , Guid              personaId
                                    , List<Guid>        contradictionReferences
                                    , CancellationToken cancellationToken = default );

    /// <summary>
    /// Advances a memory's state along the confidence ladder:
    /// <see cref="MemoryState.Provisional"/> → <see cref="MemoryState.Reinforced"/>,
    /// <see cref="MemoryState.Reinforced"/> → <see cref="MemoryState.Canonical"/>.
    /// Other states are left unchanged. Stamps <c>LastModifiedUtc</c>.
    /// </summary>
    Task ConfirmMemoryAsync( Guid              memoryId
                           , Guid              personaId
                           , CancellationToken cancellationToken = default );
}
