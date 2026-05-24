using System.Collections.Concurrent;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaSessionManager : IPersonaSessionManager
{
    private readonly ConcurrentDictionary<string, Guid>                    _activeSessions    = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConversationMode>        _conversationModes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PersonaSnapshotOverride> _snapshotOverrides = new(StringComparer.OrdinalIgnoreCase);

    public void SetActivePersona(string conversationId, Guid personaId) =>
        _activeSessions[conversationId] = personaId;

    public Guid? GetActivePersona(string conversationId) =>
        _activeSessions.TryGetValue(conversationId, out var personaId) ? personaId : null;

    public void ClearActivePersona(string conversationId) =>
        _activeSessions.TryRemove(conversationId, out _);

    public bool IsPersonaConversation(string conversationId) =>
        _activeSessions.ContainsKey(conversationId);

    public void SetConversationMode(string conversationId, ConversationMode mode) =>
        _conversationModes[conversationId] = mode;

    public ConversationMode GetConversationMode(string conversationId) =>
        _conversationModes.TryGetValue(conversationId, out var mode)
            ? mode
            : ConversationMode.FreeConversation;

    public void SetSnapshotOverride(string conversationId, PersonaSnapshotOverride snapshotOverride) =>
        _snapshotOverrides[conversationId] = snapshotOverride;

    public PersonaSnapshotOverride? GetSnapshotOverride(string conversationId) =>
        _snapshotOverrides.TryGetValue(conversationId, out var snapshotOverride) ? snapshotOverride : null;

    public void ClearSnapshotOverride(string conversationId) =>
        _snapshotOverrides.TryRemove(conversationId, out _);
}
