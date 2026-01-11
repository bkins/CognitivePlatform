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

    public async Task<string> SendAsync (string            prompt
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

        var requestBody = new
                          {
                                  model  = selectedModel
                                , prompt = prompt
                                , stream = false
                          };

        var endpoint = _settings.Endpoint?.TrimEnd('/') ?? "http://localhost:11434";
        using var request = new HttpRequestMessage(HttpMethod.Post
                                                 , $"{endpoint}/api/generate")
                            {
                                    Content = JsonContent.Create(requestBody)
                            };
        Console.WriteLine($"{endpoint}/api/generate - Request - Started:");
        Console.WriteLine($"   RequestUri: {request.RequestUri}");
        Console.WriteLine($"   Content: {request.Content}");
        Console.WriteLine($"   ContentType: {request.Content.Headers.ContentType}");
        Console.WriteLine($"   requestBody: {requestBody.ToString()[1..60]}...");
        
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
        if (json.response.DoesNotHaveValueOrIsNullOrEmpty())
            return json.response!;

        // Fallback: return concatenated string (if future formats change)
        return json.response ?? string.Empty;
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
        public string? response { get; set; }
        public bool    done     { get; set; }

        // future-proofing for miscellaneous fields
        public string? model      { get; set; }
        public string? created_at { get; set; }
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