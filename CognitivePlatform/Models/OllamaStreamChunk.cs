using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Models;

public sealed class OllamaStreamChunk
{
    [JsonPropertyName("response")]
    public string? Response { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}