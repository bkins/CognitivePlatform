using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Options;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// LLM client targeting the Groq cloud API (free tier).
/// Groq exposes an OpenAI-compatible /v1/chat/completions endpoint,
/// which means responses arrive in under 3 seconds even for large models.
///
/// API key is loaded from user-secrets (development) or environment
/// variables (production) â€” never from appsettings.json.
///
/// After each successful response the rate-limit headers are captured
/// and written to <see cref="IGroqUsageTracker"/> for downstream consumers.
/// </summary>
public class GroqLlmClient : ILlmClient
{
    private readonly HttpClient          _httpClient;
    private readonly LlmClientSettings   _settings;
    private readonly IGroqUsageTracker   _usageTracker;

    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                {
                                                                        PropertyNameCaseInsensitive = true
                                                                };

    // ----------------------------------------------------------------
    // Groq rate-limit header names
    // ----------------------------------------------------------------
    private const string HeaderRequestLimit      = "x-ratelimit-limit-requests";
    private const string HeaderRequestsRemaining = "x-ratelimit-remaining-requests";
    private const string HeaderRequestsReset     = "x-ratelimit-reset-requests";
    private const string HeaderTokenLimit        = "x-ratelimit-limit-tokens";
    private const string HeaderTokensRemaining   = "x-ratelimit-remaining-tokens";
    private const string HeaderTokensReset       = "x-ratelimit-reset-tokens";

    public GroqLlmClient( HttpClient                  httpClient
                        , IOptions<LlmClientSettings> settings
                        , IGroqUsageTracker            usageTracker )
    {
        _httpClient   = httpClient;
        _settings     = settings.Value;
        _usageTracker = usageTracker;

        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.Timeout);

        ConfigureAuthHeader();
    }

    public async Task<string> SendAsync( string            prompt
                                       , string?           model             = null
                                       , CancellationToken cancellationToken = default )
    {
        var selectedModel = model.HasValue()
                                    ? model!.Trim()
                                    : _settings.Groq.Model;

        var requestBody = new GroqChatRequest
                          {
                                  Model    = selectedModel
                                , Messages =
                                  [
                                          new GroqMessage { Role = "user", Content = prompt }
                                  ]
                          };

        var endpoint = $"{_settings.Groq.Endpoint.TrimEnd('/')}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                            {
                                    Content = JsonContent.Create(requestBody, options: JsonOptions)
                            };

        using var response = await _httpClient.SendAsync(request
                                                       , HttpCompletionOption.ResponseHeadersRead
                                                       , cancellationToken);

        // Capture rate-limit headers regardless of success/failure â€”
        // even a 429 response carries the current window state.
        CaptureUsageHeaders(response.Headers);

        if (response.IsSuccessStatusCode.Not())
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Groq API returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content
                                   .ReadFromJsonAsync<GroqChatResponse>(JsonOptions
                                                                      , cancellationToken);

        if (result?.Choices is not { Count: > 0 })
            throw new InvalidOperationException("Groq returned no choices in response.");

        return result.Choices[0].Message?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamAsync( string                                     prompt
                                                     , string?                                    model             = null
                                                     , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        // Groq supports streaming via SSE. For now we fall back to a
        // non-streaming call and yield the whole response as one chunk.
        // Streaming can be implemented here in a future iteration.
        var result = await SendAsync(prompt, model, cancellationToken);
        yield return result;
    }

    public async Task<LlmModelProbeResult> ProbeAsync( string            model
                                                     , CancellationToken ct = default )
    {
        try
        {
            var requestBody = new GroqChatRequest
                              {
                                      Model    = model
                                    , Messages =
                                      [
                                              new GroqMessage { Role = "user", Content = "hi" }
                                      ]
                                    , MaxTokens = 1
                              };

            var endpoint = $"{_settings.Groq.Endpoint.TrimEnd('/')}/chat/completions";

            using var response = await _httpClient.PostAsJsonAsync(endpoint
                                                                 , requestBody
                                                                 , JsonOptions
                                                                 , ct);

            CaptureUsageHeaders(response.Headers);

            if (response.IsSuccessStatusCode)
                return new LlmModelProbeResult(model, true);

            var text = await response.Content.ReadAsStringAsync(ct);
            return new LlmModelProbeResult(model, false, $"HTTP {(int)response.StatusCode}: {text}");
        }
        catch (Exception ex)
        {
            return new LlmModelProbeResult(model, false, ex.Message);
        }
    }

    // ----------------------------------------------------------------
    // Header capture
    // ----------------------------------------------------------------

    private void CaptureUsageHeaders(HttpResponseHeaders headers)
    {
        var requestsResetRaw = ReadHeader(headers, HeaderRequestsReset);
        var tokensResetRaw   = ReadHeader(headers, HeaderTokensReset);

        var snapshot = new GroqUsageSnapshot
                       {
                               RequestLimit      = ReadInt(headers, HeaderRequestLimit)
                             , RequestsRemaining  = ReadInt(headers, HeaderRequestsRemaining)
                             , RequestsResetRaw   = requestsResetRaw
                             , RequestsResetAt    = ToResetTime(requestsResetRaw)
                             , TokenLimit         = ReadInt(headers, HeaderTokenLimit)
                             , TokensRemaining    = ReadInt(headers, HeaderTokensRemaining)
                             , TokensResetRaw     = tokensResetRaw
                             , TokensResetAt      = ToResetTime(tokensResetRaw)
                             , CapturedAt         = DateTimeOffset.UtcNow
                       };

        _usageTracker.Update(snapshot);
    }

    private static string ReadHeader(HttpResponseHeaders headers, string name)
    {
        return headers.TryGetValues(name, out var values)
                       ? values.FirstOrDefault() ?? string.Empty
                       : string.Empty;
    }

    private static int ReadInt(HttpResponseHeaders headers, string name)
    {
        var raw = ReadHeader(headers, name);
        return int.TryParse(raw, out var value) ? value : 0;
    }

    private static DateTimeOffset? ToResetTime(string raw)
    {
        if (raw.HasNoValue()) return null;

        var duration = GroqResetTimeParser.Parse(raw);
        return duration == TimeSpan.Zero ? null : DateTimeOffset.UtcNow.Add(duration);
    }

    // ----------------------------------------------------------------
    // Auth
    // ----------------------------------------------------------------

    private void ConfigureAuthHeader()
    {
        var apiKey = _settings.Groq.ApiKey;

        if (apiKey.HasNoValue())
            return;

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ----------------------------------------------------------------
    // Request / response models (OpenAI-compatible schema)
    // ----------------------------------------------------------------

    private sealed class GroqChatRequest
    {
        [JsonPropertyName("model")]    public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")] public List<GroqMessage> Messages { get; set; } = [];

        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }
    }

    private sealed class GroqMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class GroqChatResponse
    {
        [JsonPropertyName("choices")] public List<GroqChoice> Choices { get; set; } = [];
    }

    private sealed class GroqChoice
    {
        [JsonPropertyName("message")] public GroqMessage? Message { get; set; }
    }
}