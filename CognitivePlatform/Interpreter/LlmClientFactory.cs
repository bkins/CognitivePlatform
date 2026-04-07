using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Resolves the active ILlmClient implementation at runtime based on
/// LlmClientSettings.Provider. This keeps Program.cs clean and makes
/// it trivial to add further providers later.
/// </summary>
public class LlmClientFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly LlmClientSettings  _settings;
    private readonly IGroqUsageTracker  _usageTracker;

    public LlmClientFactory( IHttpClientFactory          httpFactory
                           , IOptions<LlmClientSettings> settings
                           , IGroqUsageTracker           usageTracker )
    {
        _httpFactory  = httpFactory;
        _settings     = settings.Value;
        _usageTracker = usageTracker;
    }

    public ILlmClient Create()
    {
        var options = Options.Create(_settings);

        return _settings.Provider switch
        {
                LlmProvider.Groq   => new GroqLlmClient(_httpFactory.CreateClient("Groq")
                                                      , options
                                                      , _usageTracker)
              , LlmProvider.Ollama => new OllamaLlmClient(_httpFactory.CreateClient("Ollama")
                                                        , options)
              , _ => throw new InvalidOperationException(
                         $"Unknown LLM provider: {_settings.Provider}")
        };
    }
}