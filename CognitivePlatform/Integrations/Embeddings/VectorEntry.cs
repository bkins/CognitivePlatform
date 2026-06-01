namespace CognitivePlatform.Api.Integrations.Embeddings;

public sealed record VectorEntry
{
    public string         Id          { get; init; } = string.Empty;
    public string         Domain      { get; init; } = string.Empty;
    public string         ReferenceId { get; init; } = string.Empty;
    public string         Text        { get; init; } = string.Empty;
    public float[]        Embedding   { get; init; } = [];
    public DateTimeOffset EmbeddedAt  { get; init; } = DateTimeOffset.UtcNow;
}
