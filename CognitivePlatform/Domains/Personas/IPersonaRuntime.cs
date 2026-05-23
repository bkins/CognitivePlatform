using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaRuntime
{
    Task<string>                    BuildSystemPromptAsync(CanonicalPersona       persona, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonaMemory>> RetrieveRelevantMemoriesAsync(CanonicalPersona persona, string userMessage, int maxMemories = 10, CancellationToken cancellationToken = default);
    Task<PersonaConversationContext>   BuildConversationContextAsync(Guid             personaId, string userMessage, CancellationToken cancellationToken = default);
}
