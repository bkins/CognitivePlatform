namespace CognitivePlatform.Admin.Services.ToolScript.Models;

public sealed class ToolMetadata
{
    public string  Name                 { get; init; } = "";
    public string  Category             { get; init; } = "";
    public string  Description          { get; init; } = "";
    public int     Order                { get; init; }
    public string? Icon                 { get; init; }
    public bool    Hidden               { get; init; }
    public bool    RequiresConfirmation { get; init; }
}