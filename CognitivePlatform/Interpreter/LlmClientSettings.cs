using System.Text;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Selects which LLM backend the interpreter uses.
/// Controlled via appsettings.json "LlmClient:Provider".
/// </summary>
public enum LlmProvider
{
    Ollama
  , Groq
  , Gemini
  , OpenRouter
  , Cerebras
}

// Bound to the "LlmClient" section in appsettings.json.
public class LlmClientSettings
{
    /// <summary>
    /// Selects the active LLM backend. Defaults to Ollama for local dev.
    /// </summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;

    // ----------------------------------------------------------------
    // Ollama settings
    // ----------------------------------------------------------------

    /// <summary>
    /// Base URL for the Ollama server.
    /// </summary>
    public string Endpoint { get; set; } = "http://192.168.0.33:11434";

    /// <summary>
    /// Legacy single-model field — kept for backwards compatibility.
    /// Prefer DefaultModel going forward.
    /// </summary>
    public string Model { get; set; } = "llama3.1:8b";

    /// <summary>
    /// Server-side default model. Overwritten at startup by the probe
    /// if SortedAllowedModels is populated.
    /// </summary>
    public string DefaultModel { get; set; } = "qwen2.5:14b";

    /// <summary>
    /// Optional allowlist for model governance. If populated, only
    /// these models (in priority order) are considered usable.
    /// </summary>
    public List<string> SortedAllowedModels { get; set; } = new();

    /// <summary>
    /// HTTP timeout in seconds applied to the LLM client.
    /// </summary>
    public double Timeout { get; set; } = 2000;

    // ----------------------------------------------------------------
    // Groq settings
    // ----------------------------------------------------------------

    public GroqSettings        Groq        { get; set; } = new();
    public GeminiSettings      Gemini      { get; set; } = new();
    public OpenRouterSettings  OpenRouter  { get; set; } = new();
    public CerebrasSettings    Cerebras    { get; set; } = new();
}

public class GeminiSettings
{
    /// <summary>
    /// Google AI Studio API key. Load from user-secrets or environment — never commit.
    /// Obtain from https://aistudio.google.com/app/apikey
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gemini model to use. Google exposes an OpenAI-compatible endpoint so the
    /// same request/response schema as Groq applies.
    /// Recommended: "gemini-2.5-flash" (fast, free tier).
    /// Full list: https://ai.google.dev/gemini-api/docs/models
    /// </summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile"; //"gemini-3.1-pro-preview";

    /// <summary>
    /// Google's OpenAI-compatible base URL.
    /// Unlikely to change but kept configurable.
    /// </summary>
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";
    
    
    public string Provider { get; set; } = "Gemini";
}

public class OpenRouterSettings
{
    /// <summary>
    /// OpenRouter API key. Load from user-secrets or environment — never commit.
    /// Obtain from https://openrouter.ai/keys
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default model routed through OpenRouter.
    /// Full list: https://openrouter.ai/models
    /// </summary>
    public string Model { get; set; } = "openai/gpt-4o-mini";

    /// <summary>
    /// OpenRouter base URL. Unlikely to change but kept configurable.
    /// </summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";
}

public class CerebrasSettings
{
    /// <summary>
    /// Cerebras Cloud API key. Load from user-secrets or environment — never commit.
    /// Obtain from https://cloud.cerebras.ai
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Cerebras model to use.
    /// Full list: https://inference-docs.cerebras.ai/introduction
    /// </summary>
    public string Model { get; set; } = "llama3.1-8b";

    /// <summary>
    /// Cerebras Cloud inference base URL. Unlikely to change but kept configurable.
    /// </summary>
    public string Endpoint { get; set; } = "https://api.cerebras.ai/v1";
}

public class GroqSettings
{
    /// <summary>
    /// Groq API key. In production load from user-secrets or environment.
    /// Never commit a real key to appsettings.json.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Groq model to use for interpretation.
    /// Recommended: "llama-3.3-70b-versatile" or "qwen-qwq-32b"
    /// Full list: https://console.groq.com/docs/models
    /// </summary>
    public string? Model { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>
    /// Groq API base URL. Unlikely to change but kept configurable.
    /// </summary>
    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";

    public string Provider { get; set; } = "Groq";
}