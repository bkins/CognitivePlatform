using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaSessionManager
{
    void   SetActivePersona(string conversationId, Guid personaId);
    Guid?  GetActivePersona(string conversationId);
    void   ClearActivePersona(string conversationId);
    bool   IsPersonaConversation(string conversationId);

    void             SetConversationMode(string conversationId, ConversationMode mode);
    ConversationMode GetConversationMode(string conversationId);

    void                   SetSnapshotOverride(string conversationId, PersonaSnapshotOverride snapshotOverride);
    PersonaSnapshotOverride? GetSnapshotOverride(string conversationId);
    void                   ClearSnapshotOverride(string conversationId);
}
