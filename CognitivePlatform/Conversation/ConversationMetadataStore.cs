using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Conversation;

public class ConversationMetadataStore : IConversationMetadataStore
{
    private const string Partition  = "conversation_meta";
    private const string KeyPrefix  = "convmeta:";

    private readonly IObjectStore _store;

    public ConversationMetadataStore(IObjectStore store)
    {
        _store = store;
    }

    public async Task UpsertAsync(ConversationMetadata metadata)
    {
        await _store.Save(metadata, partitionKey: Partition, id: StorageKey(metadata.ConversationId));
    }

    public Task<ConversationMetadata?> GetAsync(string conversationId)
    {
        return Task.FromResult(_store.Get<ConversationMetadata>(StorageKey(conversationId), partitionKey: Partition));
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
        _store.SoftDelete<ConversationMetadata>(StorageKey(conversationId), partitionKey: Partition);
        return Task.CompletedTask;
    }

    // Prefix keeps metadata IDs in their own namespace so a session ID that matches
    // another domain object's ID cannot trigger the ON CONFLICT(Id) upsert path
    // and overwrite that object's row.
    private static string StorageKey(string conversationId) => $"{KeyPrefix}{conversationId}";
}
