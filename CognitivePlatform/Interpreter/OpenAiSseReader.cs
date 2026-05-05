using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

internal static class OpenAiSseReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                {
                                                                        PropertyNameCaseInsensitive = true
                                                                };

    internal static async IAsyncEnumerable<string> ReadChunksAsync(
            Stream                                     stream
          , [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null
            && cancellationToken.IsCancellationRequested == false)
        {
            if (line.HasNoValue())                                    continue;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line["data: ".Length..];
            if (payload == "[DONE]") yield break;
            SseChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<SseChunk>(payload, JsonOptions); }
            catch { continue; }
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (content.HasValue()) yield return content!;
        }
    }

    private sealed class SseChunk
    {
        [JsonPropertyName("choices")] public List<SseChoice>? Choices { get; set; }
    }

    private sealed class SseChoice
    {
        [JsonPropertyName("delta")]         public SseDelta? Delta        { get; set; }
        [JsonPropertyName("finish_reason")] public string?   FinishReason { get; set; }
    }

    private sealed class SseDelta
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}