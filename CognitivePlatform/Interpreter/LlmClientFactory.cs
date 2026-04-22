using CognitivePlatform.Api.Interpreter.OpenAiCompatible;
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
              , LlmProvider.Gemini => new GeminiLlmClient(_httpFactory.CreateClient("Gemini")
                                                        , options)
              , LlmProvider.OpenRouter => new OpenAiCompatibleLlmClient(
                                                  _httpFactory.CreateClient("OpenRouter")
                                                , _settings.OpenRouter.ApiKey
                                                , _settings.OpenRouter.Endpoint
                                                , _settings.OpenRouter.Model
                                                , _settings.Timeout)
              , LlmProvider.Cerebras => new OpenAiCompatibleLlmClient(
                                                  _httpFactory.CreateClient("Cerebras")
                                                , _settings.Cerebras.ApiKey
                                                , _settings.Cerebras.Endpoint
                                                , _settings.Cerebras.Model
                                                , _settings.Timeout)
              , _ => throw new InvalidOperationException(
                         $"Unknown LLM provider: {_settings.Provider}")
        };
    }
}