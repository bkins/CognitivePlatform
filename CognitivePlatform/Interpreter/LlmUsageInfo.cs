namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Per-response token usage returned in the response body by OpenAI-compatible
/// providers (Groq, Gemini, OpenRouter, Cerebras). Populated by each LLM client
/// from the "usage" object in the JSON payload.
///
/// Ollama uses different field names (prompt_eval_count / eval_count) and those
/// are mapped into this same structure by OllamaLlmClient.
///
/// All counts default to zero when the provider did not return usage data.
/// </summary>
public sealed class LlmUsageInfo
{
    /// <summary>Tokens consumed by the prompt / system message.</summary>
    public int PromptTokens     { get; init; }

    /// <summary>Tokens produced by the model in its reply.</summary>
    public int CompletionTokens { get; init; }

    /// <summary>PromptTokens + CompletionTokens (as reported by the provider).</summary>
    public int TotalTokens      { get; init; }

    /// <summary>Sentinel returned when no usage data was available.</summary>
    public static readonly LlmUsageInfo Empty = new();
}
