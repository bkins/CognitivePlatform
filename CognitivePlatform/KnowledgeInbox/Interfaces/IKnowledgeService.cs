namespace CognitivePlatform.Api.KnowledgeInbox.Interfaces;

public interface IKnowledgeService
{
    IReadOnlyList<KnowledgeItemDto> GetKnowledge (KnowledgeQuery    query
                                                , CancellationToken ct);

    IReadOnlyList<ObjectHeader> ListHeaders (string          type
                                           , DateTimeOffset? fromUtc = null
                                           , DateTimeOffset? toUtc   = null);


    void Archive (Guid              id
                , CancellationToken ct);
}