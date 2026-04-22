namespace CognitivePlatform.Api.KnowledgeInbox.Interfaces;

public interface IKnowledgeSource
{
    KnowledgeKind Kind { get; }

    IEnumerable<KnowledgeItemDto> GetKnowledgeItems (KnowledgeQuery    query
                                                   , CancellationToken ct);

    IReadOnlyList<ObjectHeader> ListHeaders (DateTimeOffset? fromUtc
                                            , DateTimeOffset? toUtc);

    void Archive (Guid              id
                , CancellationToken ct);

}
