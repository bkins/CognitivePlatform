using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Options;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// LLM client targeting Google's Gemini API via its OpenAI-compatible endpoint.
///
/// Google exposes /v1beta/openai/chat/completions which accepts the same
/// request/response schema as Groq/OpenAI, so only the base URL and API
/// key handling differ from GroqLlmClient.
///
/// API key is loaded from user-secrets (development) or environment variables
/// (production) — never from appsettings.json.
/// Get a key at https://aistudio.google.com/app/apikey
/// </summary>
public class GeminiLlmClient : ILlmClient
{
    private readonly HttpClient        _httpClient;
    private readonly LlmClientSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                {
                                                                        PropertyNameCaseInsensitive = true
                                                                };

    public GeminiLlmClient( HttpClient                  httpClient
                          , IOptions<LlmClientSettings> settings )
    {
        _httpClient = httpClient;
        _settings   = settings.Value;

        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.Timeout);

        ConfigureAuthHeader();
    }

    public async Task<string> SendAsync( string            prompt
                                       , string?           model             = null
                                       , CancellationToken cancellationToken = default )
    {
        var selectedModel = model.HasValue()
                                    ? model!.Trim()
                                    : _settings.Gemini.Model;

        var requestBody = new ChatRequest
                          {
                                  Model = selectedModel
                                , Messages =
                                  [
                                          new ChatMessage
                                          {
                                                  Role    = "user"
                                                , Content = prompt
                                          }
                                  ]
                          };

        var endpoint = $"{_settings.Gemini.Endpoint.TrimEnd('/')}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                            {
                                    Content = JsonContent.Create(requestBody, options: JsonOptions)
                            };

        using var response = await _httpClient.SendAsync(request
                                                       , HttpCompletionOption.ResponseHeadersRead
                                                       , cancellationToken);

        if (response.IsSuccessStatusCode.Not())
        {
            var errorBody    = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorMessage = string.Empty;
            
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable
                                    or HttpStatusCode.TooManyRequests)
            {
                errorMessage = response.StatusCode switch
                {
                        HttpStatusCode.TooManyRequests    => "Rate limit exceeded for the day. Please try again tomorrow."
                      , HttpStatusCode.ServiceUnavailable => "Service is unavailable."
                      , _                                 => errorMessage
                };
#if DEBUG
                errorMessage += $"\nHTTP {(int)response.StatusCode}: {errorBody}";
#endif
                return errorMessage;
            }
            
            throw new HttpRequestException($"Gemini API returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content
                                   .ReadFromJsonAsync<ChatResponse>(JsonOptions
                                                                  , cancellationToken);

        if (result?.Choices is not { Count: > 0 })
            throw new InvalidOperationException("Gemini returned no choices in response.");

        return result.Choices[0].Message?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamAsync( string                                     prompt
                                                     , string?                                    model             = null
                                                     , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        var selectedModel = model.HasValue()
                                    ? model!.Trim()
                                    : _settings.Gemini.Model;

        var requestBody = new ChatRequest
                          {
                                  Model    = selectedModel
                                , Messages =
                                  [
                                          new ChatMessage { Role = "user", Content = prompt }
                                  ]
                                , Stream   = true
                          };

        var endpoint = $"{_settings.Gemini.Endpoint.TrimEnd('/')}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                            {
                                    Content = JsonContent.Create(requestBody, options: JsonOptions)
                            };

        using var response = await _httpClient.SendAsync(request
                                                       , HttpCompletionOption.ResponseHeadersRead
                                                       , cancellationToken);

        if (response.IsSuccessStatusCode.Not())
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Gemini API returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var chunk in OpenAiSseReader.ReadChunksAsync(responseStream, cancellationToken))
            yield return chunk;
    }

    public async Task<LlmModelProbeResult> ProbeAsync( string            model
                                                     , CancellationToken ct = default )
    {
        try
        {
            var requestBody = new ChatRequest
                              {
                                      Model    = model
                                    , Messages =
                                      [
                                              new ChatMessage { Role = "user", Content = "hi" }
                                      ]
                                    , MaxTokens = 1
                              };

            var endpoint = $"{_settings.Gemini.Endpoint.TrimEnd('/')}/chat/completions";

            using var response = await _httpClient.PostAsJsonAsync(endpoint
                                                                 , requestBody
                                                                 , JsonOptions
                                                                 , ct);

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
    // Auth
    // ----------------------------------------------------------------

    private void ConfigureAuthHeader()
    {
        var apiKey = _settings.Gemini.ApiKey;

        if (apiKey.HasNoValue())
            return;

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ----------------------------------------------------------------
    // Request / response models — OpenAI-compatible schema
    // (identical to Groq; kept local to avoid cross-client coupling)
    // ----------------------------------------------------------------

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]    public string           Model    { get; set; } = string.Empty;
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Stream { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<ChatChoice> Choices { get; set; } = [];
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
