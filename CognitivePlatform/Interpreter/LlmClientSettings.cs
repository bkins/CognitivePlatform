namespace CognitivePlatform.Api.Interpreter;

// (Referenced in appsettings.json later in Phase 2.)
// This will be bound to appsettings.json (also added later—Phase 2 Epic 1, Feature: “model settings”).
public class LlmClientSettings
{
    /// <summary>
    /// The base URL for the LLM provider (e.g., Ollama).
    /// </summary>
    public string Endpoint { get; set; } = "http://192.168.0.33:11434"; //"http://localhost:11434"; //

    /// <summary>
    /// The model name to use for all interpreter calls.
    /// </summary>
    public string Model { get; set; } = "llama3.1:8b"; //"phi-3:mini"; //"qwen2.5:14b"; //""llama3.1:8b"; // "llama3"; // 
    /*Don't use these (not smart enough):
        - tinyllama
        - phi-2
        - qwen:0.5b / 1.5b
    */
    /*Best but minimal options:
     1. phi-3:mini
     2. phi-3:mini-4k
     3. llama3.1:8b-instruct
     */

    // Server-side default model
    public string DefaultModel { get; set; } = "llama3";

    // Optional allowlist for safety / governance
    public List<string> SortedAllowedModels { get; set; } = new();

 
    public double Timeout { get; set; } = 500;
}