using System.Collections.Concurrent;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaSessionManager : IPersonaSessionManager
{
    private readonly ConcurrentDictionary<string, Guid> _activeSessions = new(StringComparer.OrdinalIgnoreCase);

    public void SetActivePersona(string conversationId, Guid personaId) =>
        _activeSessions[conversationId] = personaId;

    public Guid? GetActivePersona(string conversationId) =>
        _activeSessions.TryGetValue(conversationId, out var personaId) ? personaId : null;

    public void ClearActivePersona(string conversationId) =>
        _activeSessions.TryRemove(conversationId, out _);

    public bool IsPersonaConversation(string conversationId) =>
        _activeSessions.ContainsKey(conversationId);
}
