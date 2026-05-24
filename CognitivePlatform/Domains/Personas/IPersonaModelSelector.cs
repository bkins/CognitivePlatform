using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaModelSelector
{
    string SelectModel(CanonicalPersona persona, PersonaStabilityScore stabilityScore);
    string AdaptPromptForProvider(string prompt, ModelCapabilityProfile profile);
}
