namespace CognitivePlatform.Api.SystemPromptLogging.Models;

public sealed class PromptLogEntry
{
    public string  Description { get; init; } = string.Empty;
    public string  Timestamp   { get; init; } = string.Empty;
    public string  PromptFile  { get; init; } = string.Empty;
    public string? Model       { get; init; } = string.Empty;
    public string? Provider    { get; init; } = string.Empty;
    
    public int?    TokenCount { get; init; }
}