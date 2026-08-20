namespace CognitivePlatform.Api.Domains.Document;

/// <summary>
/// Splits a document string into overlapping fixed-size chunks, preserving
/// sentence boundaries where possible.
/// Chunk size ~2000 chars (≈512 tokens), overlap ~200 chars (≈50 tokens).
/// </summary>
public sealed class DocumentChunkingService
{
    private const int ChunkSize    = 2000;
    private const int OverlapSize  = 200;

    private static readonly string[] SentenceSplitters = [".\n", ". ", "\n\n", "\r\n\r\n"];

    public IReadOnlyList<string> Chunk(string text)
    {
        if (text.HasNoValue())
            return [];

        var chunks = new List<string>();
        var start  = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + ChunkSize, text.Length);

            if (end < text.Length)
                end = FindSentenceBoundary(text, end);

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
                chunks.Add(chunk);

            if (end >= text.Length)
                break;

            // Advance by stride (ChunkSize - OverlapSize), but never go backward.
            var stride = ChunkSize - OverlapSize;
            start      = Math.Max(start + stride, end - OverlapSize);
        }

        return chunks;
    }

    private static int FindSentenceBoundary(string text, int preferredEnd)
    {
        var searchFrom = Math.Max(preferredEnd - OverlapSize, 0);

        foreach (var splitter in SentenceSplitters)
        {
            var index = text.LastIndexOf(splitter, preferredEnd, preferredEnd - searchFrom, StringComparison.Ordinal);
            if (index > searchFrom)
                return index + splitter.Length;
        }

        return preferredEnd;
    }
}
