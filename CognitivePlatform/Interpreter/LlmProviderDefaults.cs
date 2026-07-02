using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Per-provider default model names used when a session switches providers
/// but hasn't pinned a specific model yet.
///
/// CHANGE: this used to be its own POCO bound directly from a separate
/// "Llm:Defaults" config section. That duplicated the per-provider model
/// names already configured under "LlmClient:{Provider}:Model" (the same
/// values <see cref="LlmClientFactory"/> uses to build each <see cref="ILlmClient"/>) —
/// two places to update, with no guard against them drifting apart. This class
/// is now a thin read-through over the existing <see cref="LlmClientSettings"/>
/// values, so there is exactly one place the model defaults live.
/// </summary>
public class LlmProviderDefaults
{
    private readonly LlmClientSettings _settings;

    public LlmProviderDefaults(IOptions<LlmClientSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// Returns the default model for the given provider, sourced from the
    /// corresponding LlmClientSettings entry. Returns an empty string (never
    /// null) if the provider's model field hasn't been set — callers that
    /// previously null-checked this can keep doing so safely, since
    /// string.IsNullOrWhiteSpace covers both null and empty.
    /// </summary>
    public string? For(LlmProvider provider)
    {
        return provider switch
        {
                // Ollama has no nested settings object like the other providers (Model and
                // DefaultModel both live directly on LlmClientSettings). DefaultModel is used
                // here per LlmClientSettings' own doc comment, which marks Model as legacy and
                // says to "prefer DefaultModel going forward." Both fields are static config
                // values — confirmed by inspecting LlmStartupProbe, which despite an XML doc
                // comment elsewhere claiming it "overwrites DefaultModel at startup," only ever
                // writes to LlmModelCatalog and never touches LlmClientSettings at all. That doc
                // comment describes behavior that doesn't exist in the current code, so there's
                // no live/probed value to prefer over the static one here.
                LlmProvider.Ollama     => _settings.DefaultModel
              , LlmProvider.Groq       => _settings.Groq.Model
              , LlmProvider.Gemini     => _settings.Gemini.Model
              , LlmProvider.OpenRouter => _settings.OpenRouter.Model
              , LlmProvider.Cerebras   => _settings.Cerebras.Model
              , _                      => string.Empty
        };
    }
}
