using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Conversation;

public class ConversationMetadataStore : IConversationMetadataStore
{
    private const string Partition = "conversation_meta";

    private readonly IObjectStore _store;

    public ConversationMetadataStore(IObjectStore store)
    {
        _store = store;
    }

    public async Task UpsertAsync(ConversationMetadata metadata)
    {
        await _store.Save(metadata, partitionKey: Partition, id: metadata.ConversationId);
    }

    public Task<ConversationMetadata?> GetAsync(string conversationId)
    {
        return Task.FromResult(_store.Get<ConversationMetadata>(conversationId, partitionKey: Partition));
    }

    public Task<IEnumerable<ConversationMetadata>> ListAllAsync()
    {
        var all = _store.List<ConversationMetadata>(partitionKey: Partition)
                        .Where(metadata => !metadata.IsDeleted)
                        .OrderByDescending(metadata => metadata.LastActiveUtc);

        return Task.FromResult<IEnumerable<ConversationMetadata>>(all);
    }

    public Task SoftDeleteAsync(string conversationId)
    {
        _store.SoftDelete<ConversationMetadata>(conversationId, partitionKey: Partition);
        return Task.CompletedTask;
    }
}
