using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPendingMemoryConfirmationService
{
    Task EnqueueAsync(string conversationId, PersonaMemory memory, CancellationToken cancellationToken = default);
    Task ResolveAsync(Guid personaMemoryId, string resolutionReason, CancellationToken cancellationToken = default);
    Task<PendingMemoryConfirmationSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
