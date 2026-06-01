namespace CognitivePlatform.Api.Integrations.Embeddings;

public interface IEmbeddingService
{
    bool IsAvailable { get; }

    Task<float[]>   EmbedAsync      (string                text,  CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync (IReadOnlyList<string> texts, CancellationToken ct = default);
}
