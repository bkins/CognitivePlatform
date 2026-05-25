namespace CognitivePlatform.Api.Conversation;

public interface IConversationMetadataStore
{
    Task                                   UpsertAsync     (ConversationMetadata metadata);
    Task<ConversationMetadata?>            GetAsync        (string conversationId);
    Task<IEnumerable<ConversationMetadata>> ListAllAsync   ();
    Task                                   SoftDeleteAsync (string conversationId);
}
