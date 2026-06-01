namespace CognitivePlatform.Api.Integrations.Embeddings;

public sealed record EmbeddingSettings
{
    public string OllamaBaseUrl      { get; init; } = "http://localhost:11434";
    public string EmbeddingModel     { get; init; } = "nomic-embed-text";
    public int    PingTimeoutSeconds { get; init; } = 4;
}
