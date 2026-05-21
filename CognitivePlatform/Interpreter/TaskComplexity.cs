namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Indicates the computational complexity of an LLM call, used by the router
/// to prefer a model tier appropriate for the work being done.
/// </summary>
public enum TaskComplexity
{
    /// <summary>
    /// Simple, low-reasoning tasks: text blending, weaving, classification.
    /// Prefer cheap, fast models (e.g. Flash-Lite).
    /// </summary>
    Light

    /// <summary>
    /// General-purpose reasoning and conversation.
    /// Default for most interpreter and orchestrator calls.
    /// </summary>
  , Standard

    /// <summary>
    /// Deep reasoning, large context windows, synthesis across many records.
    /// Prefer high-capability models (e.g. Gemini Pro, large Llama variants).
    /// </summary>
  , Heavy
}
