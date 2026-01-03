namespace CognitivePlatform.Api.KnowledgeInbox;

public interface IKnowledgeSource
{
    KnowledgeKind Kind { get; }

    IEnumerable<KnowledgeItemDto> GetKnowledgeItems (KnowledgeQuery    query
                                                   , CancellationToken ct);
}
