using static System.Text.Encoding;

namespace CognitivePlatform.Api.Interpreter;

public class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient        _httpClient;
    private readonly LlmClientSettings _settings;

    public OllamaLlmClient(HttpClient        httpClient
                         , LlmClientSettings settings)
    {
        _httpClient = httpClient;
        _settings   = settings;
        
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.Timeout);
    }

    public async Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var requestBody = new
                          {
                                  model  = _settings.Model
                                , prompt = prompt
                                , stream = false
                          };

        var endpoint = _settings.Endpoint?.TrimEnd('/') ?? "http://localhost:11434";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/generate")
                            {
                                    Content = JsonContent.Create(requestBody)
                            };
        // using var response = await _httpClient.PostAsJsonAsync(
        //                              $"{endpoint}/api/generate",
        //                              requestBody,
        //                              cancellationToken);
        
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        // Ollama returns a JSON object including "response" and "done"
        var json = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: cancellationToken);

        if (json is null)
            throw new InvalidOperationException("LLM returned no JSON response.");

        // Some models return null for `response` until `done = true`
        if (!string.IsNullOrWhiteSpace(json.response))
            return json.response!;

        // Fallback: return concatenated string (if future formats change)
        return json.response ?? string.Empty;
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

}