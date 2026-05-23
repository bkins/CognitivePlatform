namespace CognitivePlatform.Api.Conversation;

public interface IConversationTurnStore
{
    Task                           SaveAsync  (string conversationId, ConversationTurn turn, CancellationToken ct = default);
    IReadOnlyList<ConversationTurn> GetRecent  (string conversationId, int last);
}
