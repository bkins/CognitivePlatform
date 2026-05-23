using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Conversation;

public class ConversationTurnStore : IConversationTurnStore
{
    private readonly IObjectStore _store;

    public ConversationTurnStore(IObjectStore store)
    {
        _store = store;
    }

    public async Task SaveAsync(string conversationId, ConversationTurn turn, CancellationToken ct = default)
    {
        var entity = new PersistedConversationTurn
                     {
                             ConversationId   = conversationId
                           , UserMessage      = turn.UserMessage
                           , AssistantMessage = turn.AssistantMessage
                           , OccurredAt       = turn.OccurredAt.ToUniversalTime()
                           , Path             = turn.Path
                           , ActionName       = turn.ActionName
                           , Succeeded        = turn.Succeeded
                     };
        await _store.Save(entity, partitionKey: conversationId);
    }

    public IReadOnlyList<ConversationTurn> GetRecent(string conversationId, int last)
    {
        return _store.List<PersistedConversationTurn>(partitionKey: conversationId)
                     .TakeLast(last)
                     .Select(entity => new ConversationTurn(
                                 UserMessage:      entity.UserMessage
                               , AssistantMessage: entity.AssistantMessage
                               , OccurredAt:       entity.OccurredAt
                               , Path:             entity.Path
                               , ActionName:       entity.ActionName
                               , Succeeded:        entity.Succeeded))
                     .ToList();
    }
}
