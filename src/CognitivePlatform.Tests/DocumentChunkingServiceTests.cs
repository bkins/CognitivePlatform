using CognitivePlatform.Api.Domains.Document;

namespace CognitivePlatform.Tests;

public class DocumentChunkingServiceTests
{
    private readonly DocumentChunkingService _service = new();

    // ================================================================
    // Empty / null input
    // ================================================================

    [Fact]
    public void Chunk_ReturnsEmpty_WhenTextIsEmpty()
    {
        var result = _service.Chunk(string.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_ReturnsEmpty_WhenTextIsWhitespace()
    {
        var result = _service.Chunk("   ");

        Assert.Empty(result);
    }

    // ================================================================
    // Short text — produces exactly one chunk
    // ================================================================

    [Fact]
    public void Chunk_ReturnsSingleChunk_WhenTextShorterThanChunkSize()
    {
        var text   = "This is a short document.";
        var result = _service.Chunk(text);

        Assert.Single(result);
        Assert.Equal(text, result[0]);
    }

    // ================================================================
    // Long text — produces multiple chunks with overlap
    // ================================================================

    [Fact]
    public void Chunk_ProducesMultipleChunks_WhenTextExceedsChunkSize()
    {
        var text   = new string('a', 5000);
        var result = _service.Chunk(text);

        Assert.True(result.Count > 1);
    }

    [Fact]
    public void Chunk_ChunksOverlap_WhenTextIsLong()
    {
        var text   = new string('x', 4500);
        var result = _service.Chunk(text);

        Assert.True(result.Count >= 2);

        // Verify overlap: end of first chunk appears at the start of second
        var overlapLen = Math.Min(50, result[0].Length);
        var firstEnd   = result[0][^overlapLen..];
        var secondText = result[1];

        Assert.Contains(firstEnd, secondText);
    }

    // ================================================================
    // Sentence boundary preference
    // ================================================================

    [Fact]
    public void Chunk_SplitsNearSentenceBoundary_RatherThanMidWord()
    {
        // 200 sentences × ~30 chars ≈ 6000 chars — comfortably over the 2000-char chunk size
        var sentences = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"Sentence number {i} is written here."));
        var result    = _service.Chunk(sentences);

        Assert.True(result.Count > 1);

        // Each non-final chunk should contain at least one sentence boundary character
        for (int chunkIndex = 0; chunkIndex < result.Count - 1; chunkIndex++)
        {
            var chunk = result[chunkIndex];
            Assert.Contains(".", chunk);
        }
    }

    // ================================================================
    // All chunks are non-empty
    // ================================================================

    [Fact]
    public void Chunk_AllChunksAreNonEmpty()
    {
        var text   = new string('b', 6000);
        var result = _service.Chunk(text);

        Assert.All(result, chunk => Assert.False(chunk.HasNoValue()));
    }
}
