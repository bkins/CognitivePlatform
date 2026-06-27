namespace CognitivePlatform.Admin.Services.ToolScript.Models;

public class ToolScript
{
    public string                       ScriptPath { get; init; } = "";
    public ToolMetadata                 Metadata   { get; init; } = new();
    public IReadOnlyList<ToolParameter> Parameters { get; init; } = [];
    
    public ToolLoadStatus Status        { get; init; }
    public string?        StatusMessage { get; init; }
}