namespace CognitivePlatform.Api.Contracts;

public record CreateAgentJobRequest(string Prompt, string? ConversationId, string? Model = null);

public record CompleteAgentJobRequest(string Response, string? ConversationId);

public record FailAgentJobRequest(string Error);
