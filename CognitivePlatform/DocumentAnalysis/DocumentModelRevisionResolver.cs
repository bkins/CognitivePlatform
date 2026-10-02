using System.Text.Json;

namespace CognitivePlatform.Api.DocumentAnalysis;

/// <summary>Read-only Ollama catalog identity; never sends document text or analysis credentials.</summary>
public sealed class DocumentModelRevisionResolver(IHttpClientFactory http) : IDocumentModelRevisionResolver
{
    public async Task<string?> ResolveAsync(string endpoint, string model, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var client = http.CreateClient("Ollama");
            using var response = await client.GetAsync(endpoint.TrimEnd('/') + "/api/tags", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_048_576) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var limited = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) != 0)
            {
                if (limited.Length + count > 1_048_576) return null;
                limited.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(limited.ToArray());
            var canonical = model.Contains(':') ? model : model + ":latest";
            foreach (var entry in json.RootElement.GetProperty("models").EnumerateArray())
                if (entry.GetProperty("name").GetString() == canonical && entry.TryGetProperty("digest", out var digest))
                {
                    var value = digest.GetString();
                    return value is { Length: 64 } && value.All(Uri.IsHexDigit) ? value : null;
                }
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }
}
