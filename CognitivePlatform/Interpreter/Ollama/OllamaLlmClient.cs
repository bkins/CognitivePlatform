using System.Runtime.CompilerServices;
using System.Text.Json;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Options;
using static System.Text.Encoding;

namespace CognitivePlatform.Api.Interpreter;

public class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient        _httpClient;
    private readonly LlmClientSettings _settings;
    
    public OllamaLlmClient(HttpClient                  httpClient
                         , IOptions<LlmClientSettings> settings)
    {
        _httpClient = httpClient;
        _settings   = settings.Value;
        
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.Timeout);
    }

    public async Task<LlmResponse> SendAsync (string            prompt
                                       , string?           model             = null
                                       , CancellationToken cancellationToken = default)
    {
        // var requestBody = new
        //                   {
        //                           model  = _settings.Model
        //                         , prompt = prompt
        //                         , stream = false
        //                   };

        var selectedModel = string.IsNullOrWhiteSpace(model)
                                    ? _settings.DefaultModel
                                    : model.Trim();

        if (_settings.SortedAllowedModels
                     .HasEntries()
         && _settings.SortedAllowedModels
                     .DoesNotContainCaseInsensitive(selectedModel))
        {
            selectedModel = _settings.DefaultModel;
        }

        var requestBody = new Dictionary<string, object>
                          {
                                { "model", selectedModel }
                              , { "prompt", prompt }
                              , { "stream", false }
                          };

        if (prompt.Contains("REQUIRED JSON FORMAT:"))
        {
            requestBody["format"] = "json";
        }

        var endpoint = _settings.Endpoint?.TrimEnd('/') ?? "http://localhost:11434";
        using var request = new HttpRequestMessage(HttpMethod.Post
                                                 , $"{endpoint}/api/generate")
                            {
                                    Content = JsonContent.Create(requestBody)
                            };
        Console.WriteLine($"{endpoint}/api/generate - Request - Started:");
        Console.WriteLine($"   RequestUri: {request.RequestUri}");
        //Console.WriteLine($"   Content: {request.Content}");
        //Console.WriteLine($"   ContentType: {request.Content.Headers.ContentType}");
        var requestBodyString = requestBody.ToString() ?? string.Empty;
        var displayString = requestBodyString.Length > 60 ? requestBodyString[1..60] : requestBodyString;
        Console.WriteLine($"   requestBody: {displayString}...");
        
        using var response = await _httpClient.SendAsync(request
                                                       , HttpCompletionOption.ResponseHeadersRead
                                                       , cancellationToken);

        response.EnsureSuccessStatusCode();

        // Ollama returns a JSON object including "response" and "done"
        var json = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: cancellationToken);

        Console.WriteLine($"{endpoint}/api/generate - Complete");
        if (json is null)
            throw new InvalidOperationException("LLM returned no JSON response.");

        // Some models return null for `response` until `done = true`
        var content = (json.response.DoesNotHaveValueOrIsNullOrEmpty())
                          ? json.response!
                          : json.response ?? string.Empty;

        var usage = new LlmUsageInfo
                    {
                            PromptTokens     = json.prompt_eval_count
                          , CompletionTokens = json.eval_count
                          , TotalTokens      = json.prompt_eval_count + json.eval_count
                    };

        return new LlmResponse
               {
                       Content    = content
                     , Usage      = usage
                     , RateLimits = LlmRateLimitSnapshot.Empty
               };
    }

    public async IAsyncEnumerable<string> StreamAsync (string            prompt
                                                     , string?           model             = null
                            , [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var selectedModel = string.IsNullOrWhiteSpace(model)
                                    ? _settings.DefaultModel
                                    : model.Trim();

        if (_settings.SortedAllowedModels
                     .HasEntries()
         && _settings.SortedAllowedModels
                     .DoesNotContainCaseInsensitive(selectedModel))
        {
            selectedModel = _settings.DefaultModel;
        }

        var endpoint = _settings.Endpoint.TrimEnd('/');

        var requestBody = new
                          {
                                  model = selectedModel
                                , prompt = prompt
                                , stream = true
                          };

        using var request = new HttpRequestMessage(HttpMethod.Post
                                                 , $"{endpoint}/api/generate")
                            {
                                    Content = JsonContent.Create(requestBody)
                            };

        using var response = await _httpClient.SendAsync(request
                                                       , HttpCompletionOption.ResponseHeadersRead
                                                       , cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var       reader = new StreamReader(stream);

        string? line;
        
        while ((line = await reader.ReadLineAsync()) is not null 
            && cancellationToken.IsCancellationRequested.Not())
        {
            if (line.DoesNotHaveValueOrIsNullOrEmpty()) continue;

            OllamaStreamChunk? chunk;

            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line);
            }
            catch
            {
                continue; // tolerate malformed lines
            }

            if (chunk?.Response.HasValue() ?? false)
            {
                yield return chunk.Response ?? string.Empty;
            }

            if (chunk?.Done == true)
                yield break;
        }
    }

    private sealed class OllamaResponse
    {
        // from the Ollama spec
        public string? response          { get; set; }
        public bool    done              { get; set; }

        // Token usage fields (Ollama names differ from OpenAI)
        public int     prompt_eval_count { get; set; }
        public int     eval_count        { get; set; }

        // future-proofing for miscellaneous fields
        public string? model             { get; set; }
        public string? created_at        { get; set; }
    }

    public async Task<LlmModelProbeResult> ProbeAsync (string            model
                                                     , CancellationToken ct = default)
    {
        try
        {
            var requestBody = new
                              {
                                      model, messages = new[]
                                                        {
                                                                new
                                                                {
                                                                        role = "user"
                                                                      , content = "hi"
                                                                }
                                                        }
                                    , stream = false
                                    , options = new { num_predict = 1 }
                              };

            using var response = await _httpClient.PostAsJsonAsync($"{_settings.Endpoint}/api/chat"
                                                                 , requestBody
                                                                 , ct);

            if (response.IsSuccessStatusCode) return new(model, true);
            
            var text = await response.Content.ReadAsStringAsync(ct);
            
            return new(model, false, $"HTTP {(int)response.StatusCode}: {text}");

        }
        catch (Exception ex)
        {
            return new(model, false, ex.Message);
        }
    }

}
