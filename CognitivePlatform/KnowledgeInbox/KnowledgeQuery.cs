namespace CognitivePlatform.Api.KnowledgeInbox;

public sealed class KnowledgeQuery
{
    public KnowledgeKind?   Kind   { get; init; }
    public KnowledgeStatus? Status { get; init; }
    public string?          Tag    { get; init; }
    public string?          Search { get; init; }
}
    