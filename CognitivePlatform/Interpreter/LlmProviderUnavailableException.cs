namespace CognitivePlatform.Api.Interpreter;

/// <summary>Preserves the router's display-safe failure message and diagnostic identifier.</summary>
public sealed class LlmProviderUnavailableException(string message) : Exception(message);
