using System.Text.Json;

namespace CognitivePlatform.Api.Contracts;

public static class ConversationStreamFrame
{
    public const string HeaderName = "X-CP-Stream-Format";
    public const string JsonStringFormat = "json-string-v1";

    public static string Encode(string chunk, bool jsonStrings)
        => $"data: {(jsonStrings ? JsonSerializer.Serialize(chunk) : chunk)}\n\n";
}
