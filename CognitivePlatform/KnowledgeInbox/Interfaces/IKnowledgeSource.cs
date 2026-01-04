namespace CognitivePlatform.Api.KnowledgeInbox.Interfaces;

public interface IKnowledgeSource
{
    KnowledgeKind Kind { get; }

    IEnumerable<KnowledgeItemDto> GetKnowledgeItems (KnowledgeQuery    query
                                                   , CancellationToken ct);

    void Archive (Guid              id
                , CancellationToken ct);

}
