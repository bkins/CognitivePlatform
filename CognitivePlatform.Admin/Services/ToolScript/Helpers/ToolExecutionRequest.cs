namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

public sealed class ToolExecutionRequest
{
    public required Models.ToolScript Tool { get; init; }

    public Dictionary<string, object?> Values { get; } = new();
}