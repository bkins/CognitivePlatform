using System.Text;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

/// <summary>
/// Verifies that OpenAiSseReader correctly parses OpenAI SSE streams into content chunks.
/// </summary>
public class OpenAiSseReaderTests
{
    // ================================================================
    // Helpers
    // ================================================================

    private static Stream ToStream(string text)
        => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static async Task<List<string>> ReadAllChunksAsync(string rawSseText)
    {
        var chunks = new List<string>();
        await foreach (var chunk in OpenAiSseReader.ReadChunksAsync(ToStream(rawSseText)))
            chunks.Add(chunk);
        return chunks;
    }

    // ================================================================
    // Happy path
    // ================================================================

    [Fact]
    public async Task ReadChunksAsync_YieldsContentFromEachDataLine()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
            "data: [DONE]\n";

        var chunks = await ReadAllChunksAsync(sse);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Hello",  chunks[0]);
        Assert.Equal(" world", chunks[1]);
    }

    [Fact]
    public async Task ReadChunksAsync_StopsAtDoneMarker()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"chunk1\"}}]}\n\n" +
            "data: [DONE]\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"should not appear\"}}]}\n";

        var chunks = await ReadAllChunksAsync(sse);

        Assert.Single(chunks);
        Assert.Equal("chunk1", chunks[0]);
    }

    [Fact]
    public async Task ReadChunksAsync_YieldsSingleChunk_WhenNoDoneMarker()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"only\"}}]}\n";

        var chunks = await ReadAllChunksAsync(sse);

        Assert.Single(chunks);
        Assert.Equal("only", chunks[0]);
    }

    // ================================================================
    // Edge cases
    // ================================================================

    [Fact]
    public async Task ReadChunksAsync_SkipsNonDataLines()
    {
        const string sse =
            ": comment\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n" +
            "data: [DONE]\n";

        var chunks = await ReadAllChunksAsync(sse);

        Assert.Single(chunks);
        Assert.Equal("hi", chunks[0]);
    }

    [Fact]
    public async Task ReadChunksAsync_SkipsChunksWithNullContent()
    {
        // A finish_reason chunk has an empty delta — no content property
        const string sse =
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n";

        var chunks = await ReadAllChunksAsync(sse);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ReadChunksAsync_ReturnsEmpty_WhenStreamIsEmpty()
    {
        var chunks = await ReadAllChunksAsync(string.Empty);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task ReadChunksAsync_SkipsMalformedJsonLines()
    {
        const string sse =
            "data: this is not json\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"valid\"}}]}\n\n" +
            "data: [DONE]\n";

        var chunks = await ReadAllChunksAsync(sse);

        Assert.Single(chunks);
        Assert.Equal("valid", chunks[0]);
    }
}
