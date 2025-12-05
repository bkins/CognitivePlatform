namespace CognitivePlatform.Api.Interpreter;

// (Referenced in appsettings.json later in Phase 2.)
// This will be bound to appsettings.json (also added later—Phase 2 Epic 1, Feature: “model settings”).
public class LlmClientSettings
{
    /// <summary>
    /// The base URL for the LLM provider (e.g., Ollama).
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// The model name to use for all interpreter calls.
    /// </summary>
    public string Model { get; set; } = "llama3"; // "llama3.1:8b"; // 
}