namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaSessionManager
{
    void   SetActivePersona(string conversationId, Guid personaId);
    Guid?  GetActivePersona(string conversationId);
    void   ClearActivePersona(string conversationId);
    bool   IsPersonaConversation(string conversationId);
}
