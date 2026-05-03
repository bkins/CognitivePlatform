namespace CognitivePlatform.Api.SystemPromptLogging.Models;

public sealed class PromptLoggingOptions
{
    public bool   Enabled  { get; set; } = false;
    public int    MaxFiles { get; set; } = 10;
    public string BasePath { get; set; } = "PromptLogs";
}