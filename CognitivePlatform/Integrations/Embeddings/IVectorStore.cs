namespace CognitivePlatform.Api.Integrations.Embeddings;

public interface IVectorStore
{
    Task SaveAsync              (VectorEntry entry,                                                      CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync (float[]  query,
                                                         int      topK   = 5,
                                                         string?  domain = null,
                                                         CancellationToken ct = default);
    Task<VectorEntry?>          GetByReferenceAsync (string domain, string referenceId,                  CancellationToken ct = default);
    Task DeleteAsync            (string id,                                                              CancellationToken ct = default);
    Task DeleteByReferenceAsync (string domain, string referenceId,                                      CancellationToken ct = default);
}

public sealed record VectorSearchResult(VectorEntry Entry, float Score);
