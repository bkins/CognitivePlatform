using CognitivePlatform.Admin.Services.ToolScript.Models;

namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

public class ToolMetadataReadResult
{
    public bool          Success      { get; init; }
    public ToolMetadata? Metadata     { get; init; }
    public string?       ErrorMessage { get; init; }
}