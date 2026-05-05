using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Abstraction over any LLM provider (Ollama, GPT, Claude, local models).
/// Phase 2: minimal surface required for structured intent interpretation.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a plain-text prompt to the underlying model and returns
    /// the model's raw text output.
    /// </summary>
    // Task<LlmResponse> SendAsync(string            prompt
    //                      , CancellationToken cancellationToken = default);

    Task<LlmResponse> SendAsync (string            prompt
                          , string?           model             = null
                          , CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync (string            prompt
                                        , string?           model             = null
                                        , CancellationToken cancellationToken = default);

    Task<LlmModelProbeResult> ProbeAsync(string model, CancellationToken ct = default);

}