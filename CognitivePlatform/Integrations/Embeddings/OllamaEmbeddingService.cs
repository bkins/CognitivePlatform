using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Integrations.Embeddings;

/// <summary>
/// IEmbeddingService that calls Ollama's /api/embeddings endpoint.
/// Registered in DI when Embedding:OllamaBaseUrl is non-empty; falls back
/// to DisconnectedEmbeddingService when the setting is absent.
/// </summary>
public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private const string ClientName = "OllamaEmbedding";

    private readonly EmbeddingSettings                _settings;
    private readonly IHttpClientFactory               _http;
    private readonly ILogger<OllamaEmbeddingService>  _logger;

    public OllamaEmbeddingService( IOptions<EmbeddingSettings>         settings
                                 , IHttpClientFactory                   http
                                 , ILogger<OllamaEmbeddingService>      logger )
    {
        _settings = settings.Value;
        _http     = http;
        _logger   = logger;
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return Task.Run(PingAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "IsAvailable check failed unexpectedly");
                return false;
            }
        }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        try
        {
            using var client  = CreateClient();
            var       payload = JsonSerializer.Serialize(new { model = _settings.EmbeddingModel, prompt = text });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("/api/embeddings", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning( "Ollama returned {Status} for embedding: {Body}"
                                  , response.StatusCode
                                  , body );
                return Array.Empty<float>();
            }

            var json   = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(json);

            return result?.Embedding ?? Array.Empty<float>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "EmbedAsync failed for text of length {Length}", text.Length);
            return Array.Empty<float>();
        }
    }

    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new float[texts.Count][];

        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct);

        return results;
    }

    private async Task<bool> PingAsync()
    {
        using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.PingTimeoutSeconds));
        using var client = CreateClient();

        try
        {
            var response = await client.GetAsync("/api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Ollama ping failed");
            return false;
        }
    }

    private HttpClient CreateClient()
    {
        var client = _http.CreateClient(ClientName);
        client.BaseAddress = new Uri(_settings.OllamaBaseUrl);
        return client;
    }

    private sealed record OllamaEmbeddingResponse(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
