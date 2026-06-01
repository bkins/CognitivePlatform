namespace CognitivePlatform.Api.Integrations.Embeddings;

/// <summary>
/// Default IEmbeddingService registered in DI when Embedding:OllamaBaseUrl is not configured.
/// IsAvailable always returns false; all embed calls return empty arrays.
/// </summary>
public sealed class DisconnectedEmbeddingService : IEmbeddingService
{
    public bool IsAvailable => false;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<float>());

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult(texts.Select(_ => Array.Empty<float>()).ToArray());
}
