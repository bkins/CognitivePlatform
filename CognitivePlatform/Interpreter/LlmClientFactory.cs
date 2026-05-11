using CognitivePlatform.Api.Interpreter.OpenAiCompatible;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Abstraction over LlmClientFactory so router-style consumers can mock
/// provider dispatch in tests.
/// </summary>
public interface ILlmClientFactory
{
    LlmProvider DefaultProvider { get; }
    ILlmClient  Create();
    ILlmClient  Create(LlmProvider provider);
}

/// <summary>
/// Resolves the active ILlmClient implementation at runtime based on
/// LlmClientSettings.Provider. This keeps Program.cs clean and makes
/// it trivial to add further providers later.
/// </summary>
public class LlmClientFactory : ILlmClientFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly LlmClientSettings  _settings;
    private readonly IGroqUsageTracker  _usageTracker;
    private readonly ILlmRateLimiter    _rateLimiter;

    public LlmClientFactory( IHttpClientFactory          httpFactory
                           , IOptions<LlmClientSettings> settings
                           , IGroqUsageTracker           usageTracker
                           , ILlmRateLimiter             rateLimiter )
    {
        _httpFactory  = httpFactory;
        _settings     = settings.Value;
        _usageTracker = usageTracker;
        _rateLimiter  = rateLimiter;
    }

    /// <summary>
    /// The provider configured as the system-wide default via LlmClient:Provider.
    /// Used as fallback when a session has not chosen a provider.
    /// </summary>
    public LlmProvider DefaultProvider => _settings.Provider;

    /// <summary>
    /// Creates the client for the configured default provider.
    /// </summary>
    public ILlmClient Create() => Create(_settings.Provider);

    /// <summary>
    /// Creates the client for a specific provider, independent of the configured default.
    /// Used by ILlmRouter to honour session-scoped provider selection.
    /// </summary>
    public ILlmClient Create(LlmProvider provider)
    {
        var options = Options.Create(_settings);

        return provider switch
        {
                LlmProvider.Groq   => new GroqLlmClient(_httpFactory.CreateClient("Groq")
                                                      , options
                                                      , _usageTracker
                                                      , _rateLimiter)
              , LlmProvider.Ollama => new OllamaLlmClient(_httpFactory.CreateClient("Ollama")
                                                        , options)
              , LlmProvider.Gemini => new GeminiLlmClient(_httpFactory.CreateClient("Gemini")
                                                        , options)
              , LlmProvider.OpenRouter => new OpenAiCompatibleLlmClient(_httpFactory.CreateClient("OpenRouter")
                                                                      , _settings.OpenRouter.ApiKey
                                                                      , _settings.OpenRouter.Endpoint
                                                                      , _settings.OpenRouter.Model
                                                                      , _settings.Timeout)
              , LlmProvider.Cerebras => new OpenAiCompatibleLlmClient(_httpFactory.CreateClient("Cerebras")
                                                                    , _settings.Cerebras.ApiKey
                                                                    , _settings.Cerebras.Endpoint
                                                                    , _settings.Cerebras.Model
                                                                    , _settings.Timeout)
              , _ => throw new InvalidOperationException($"Unknown LLM provider: {provider}")
        };
    }
}
