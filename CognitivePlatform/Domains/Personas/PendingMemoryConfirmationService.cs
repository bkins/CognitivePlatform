using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public sealed class PendingMemoryConfirmationService : IPendingMemoryConfirmationService
{
    private const string PartitionKey = "pending-memory-confirmation";

    private readonly IObjectStore _store;

    public PendingMemoryConfirmationService(IObjectStore store)
    {
        _store = store;
    }

    public async Task EnqueueAsync(string conversationId, PersonaMemory memory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = _store.Get<PendingMemoryConfirmation>(memory.Id.ToString(), PartitionKey);
        if (existing is not null)
            return;

        var confirmation = new PendingMemoryConfirmation
                           {
                                   Id              = memory.Id
                                 , PersonaId       = memory.PersonaId
                                 , PersonaMemoryId = memory.Id
                                 , ConversationId  = conversationId
                                 , CreatedUtc      = DateTime.UtcNow
                           };

        await _store.Save(confirmation, PartitionKey, confirmation.Id.ToString());
    }

    public async Task ResolveAsync(Guid personaMemoryId, string resolutionReason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var confirmation = _store.Get<PendingMemoryConfirmation>(personaMemoryId.ToString(), PartitionKey);
        if (confirmation is null || confirmation.IsResolved)
            return;

        confirmation.IsResolved       = true;
        confirmation.ResolvedUtc      = DateTime.UtcNow;
        confirmation.ResolutionReason = resolutionReason;
        await _store.Save(confirmation, PartitionKey, confirmation.Id.ToString());
    }

    public Task<PendingMemoryConfirmationSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = _store.List<PendingMemoryConfirmation>(PartitionKey).Count(confirmation => !confirmation.IsResolved);
        return Task.FromResult(new PendingMemoryConfirmationSummary(count, DateTimeOffset.UtcNow));
    }
}
