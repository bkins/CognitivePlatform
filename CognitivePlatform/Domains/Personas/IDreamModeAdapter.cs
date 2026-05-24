using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IDreamModeAdapter
{
    string BuildDreamPrompt( string                      userMessage
                           , CanonicalPersona            persona
                           , IEnumerable<PersonaMemory>  relevantMemories );

    bool IsDreamMode(PersonaConversationContext context);
}
