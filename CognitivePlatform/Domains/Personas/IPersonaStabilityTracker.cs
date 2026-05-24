using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaStabilityTracker
{
    void                 RecordRefusal(string conversationId, Guid personaId);
    void                 RecordImmersionBreak(string conversationId, Guid personaId);
    void                 RecordEmotionalDrift(string conversationId, Guid personaId);
    void                 RecordPolicyInterference(string conversationId, Guid personaId);
    PersonaStabilityScore GetScore(string conversationId, Guid personaId);
    void                 Reset(string conversationId, Guid personaId);
}
