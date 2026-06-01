using CognitivePlatform.Api.Integrations.Embeddings;

namespace CognitivePlatform.Tests;

public sealed class DisconnectedEmbeddingServiceTests
{
    private readonly DisconnectedEmbeddingService _service = new();

    // ================================================================
    // IsAvailable
    // ================================================================

    [Fact]
    public void IsAvailable_AlwaysReturnsFalse()
    {
        Assert.False(_service.IsAvailable);
    }

    // ================================================================
    // EmbedAsync
    // ================================================================

    [Fact]
    public async Task EmbedAsync_ReturnsEmptyArray_ForAnyText()
    {
        var result = await _service.EmbedAsync("anything at all");

        Assert.Empty(result);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmptyArray_ForEmptyText()
    {
        var result = await _service.EmbedAsync(string.Empty);

        Assert.Empty(result);
    }

    // ================================================================
    // EmbedBatchAsync
    // ================================================================

    [Fact]
    public async Task EmbedBatchAsync_ReturnsEmptyArrayForEachInput()
    {
        var texts = new[] { "first", "second", "third" };

        var result = await _service.EmbedBatchAsync(texts);

        Assert.Equal(3, result.Length);
        Assert.All(result, embedding => Assert.Empty(embedding));
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsEmpty_WhenNoInputs()
    {
        var result = await _service.EmbedBatchAsync(Array.Empty<string>());

        Assert.Empty(result);
    }
}
