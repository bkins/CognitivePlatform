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
    }

    public async Task<string> SendAsync(string            prompt
                                      , CancellationToken cancellationToken = default)
    {
        var requestBody = new
                          {
                              model  = _settings.Model
                            , prompt = prompt
                            , stream = false
                          };

        var endpoint = _settings.Endpoint?.TrimEnd('/') ?? "http://localhost:11434";
        using var response = await _httpClient.PostAsJsonAsync($"{endpoint}/api/generate"
                                                             , requestBody
                                                             , cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content
                                 .ReadFromJsonAsync<OllamaResponse>(cancellationToken: cancellationToken);

        return json?.response ?? string.Empty;
    }

    private sealed class OllamaResponse
    {
        public string? response { get; set; }
    }
}