namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Per-provider default model names used when a session switches providers
/// but hasn't pinned a specific model yet.
///
/// Bound from appsettings.json section "Llm:Defaults".
/// Values can be overridden per-environment.
/// </summary>
public class LlmProviderDefaults
{
    public string Ollama     { get; set; } = string.Empty;
    public string Groq       { get; set; } = string.Empty;
    public string Gemini     { get; set; } = string.Empty;
    public string OpenRouter { get; set; } = string.Empty;
    public string Cerebras   { get; set; } = string.Empty;

    /// <summary>
    /// Returns the default model for the given provider, or null if none is configured.
    /// </summary>
    public string? For(LlmProvider provider)
    {
        return provider switch
        {
                LlmProvider.Ollama     => Ollama
              , LlmProvider.Groq       => Groq
              , LlmProvider.Gemini     => Gemini
              , LlmProvider.OpenRouter => OpenRouter
              , LlmProvider.Cerebras   => Cerebras
              , _                      => null
        };
    }
}
