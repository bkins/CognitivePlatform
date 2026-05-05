using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CognitivePlatform.Api.Models;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter.OpenAiCompatible;

/// <summary>
/// Generic LLM client for any provider that exposes an OpenAI-compatible
/// /chat/completions endpoint (OpenRouter, Cerebras, etc.).
///
/// All provider-specific details (API key, base URL, default model) are
/// supplied at construction time by LlmClientFactory — this class is
/// intentionally unaware of which provider it is talking to.
/// </summary>
public class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string     _defaultModel;
    private readonly string     _endpoint;

    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                {
                                                                        PropertyNameCaseInsensitive = true
                                                                };

    public OpenAiCompatibleLlmClient( HttpClient httpClient
                                    , string     apiKey
                                    , string     endpoint
                                    , string     defaultModel
                                    , double     timeoutSeconds )
    {
        _httpClient   = httpClient;
        _endpoint     = endpoint.TrimEnd('/');
        _defaultModel = defaultModel;

        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        if (apiKey.HasValue())
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<LlmResponse> SendAsync( string            prompt
                                       , string?           model             = null
                                       , CancellationToken cancellationToken = default )
    {
        var selectedModel = model.HasValue() ? model!.Trim() : _defaultModel;

        var requestBody = new ChatRequest
                          {
                                  Model    = selectedModel
                                , Messages =
                                  [
                                          new ChatMessage { Role = "user", Content = prompt }
                                  ]
                          };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions")
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
                $"LLM API returned {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content
                                   .ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken);

        if (result?.Choices is not { Count: > 0 })
            throw new InvalidOperationException("LLM returned no choices in response.");

        var content = result.Choices[0].Message?.Content ?? string.Empty;
        var usage   = BuildUsageInfo(result.Usage);

        return new LlmResponse
               {
                       Content    = content
                     , Usage      = usage
                     , RateLimits = LlmRateLimitSnapshot.Empty
               };
    }

    public async IAsyncEnumerable<string> StreamAsync( string                                     prompt
                                                     , string?                                    model             = null
                                                     , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        var selectedModel = model.HasValue()
                                    ? model!.Trim()
                                    : _defaultModel;

        var requestBody = new ChatRequest
                          {
                                  Model    = selectedModel
                                , Messages =
                                  [
                                          new ChatMessage { Role = "user", Content = prompt }
                                  ]
                                , Stream   = true
                          };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions")
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
                $"OpenAI-compatible API returned {(int)response.StatusCode}: {errorBody}");
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

            using var response = await _httpClient.PostAsJsonAsync($"{_endpoint}/chat/completions"
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
    // Request / response models — OpenAI-compatible schema
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
        [JsonPropertyName("choices")] public List<ChatChoice>  Choices { get; set; } = [];
        [JsonPropertyName("usage")]   public ChatUsageBody?    Usage   { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private sealed class ChatUsageBody
    {
        [JsonPropertyName("prompt_tokens")]     public int PromptTokens     { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")]      public int TotalTokens      { get; set; }
    }

    private static LlmUsageInfo BuildUsageInfo(ChatUsageBody? body)
    {
        if (body is null)
            return LlmUsageInfo.Empty;

        return new LlmUsageInfo
               {
                       PromptTokens     = body.PromptTokens
                     , CompletionTokens = body.CompletionTokens
                     , TotalTokens      = body.TotalTokens
               };
    }
}
