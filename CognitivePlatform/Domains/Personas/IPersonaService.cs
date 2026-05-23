using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaService
{
    Task<CanonicalPersona>               CreateAsync(string name, string? scenarioDescription, CancellationToken cancellationToken = default);
    Task<CanonicalPersona?>              GetAsync(Guid personaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CanonicalPersona>> ListAsync(CancellationToken cancellationToken = default);
    Task<PersonaMemory>                  AddMemoryAsync(Guid personaId, string content, MemoryType type, bool userAsserted, CancellationToken cancellationToken = default);
    Task                                 UpdateMemoryStateAsync(Guid memoryId, Guid personaId, MemoryState newState, CancellationToken cancellationToken = default);
    Task<MemorySnapshot>                 CreateSnapshotAsync(Guid personaId, string name, string? notes, CancellationToken cancellationToken = default);
    Task                                 DeleteAsync(Guid personaId, CancellationToken cancellationToken = default);
}
