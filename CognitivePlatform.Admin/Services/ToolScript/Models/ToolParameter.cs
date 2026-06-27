namespace CognitivePlatform.Admin.Services.ToolScript.Models;

public sealed class ToolParameter
{
    public string                Name          { get; init; } = "";
    public string                Label         { get; init; } = "";
    public ToolParameterType     ParameterType { get; init; }
    public bool                  IsMandatory   { get; init; }
    public object?               DefaultValue  { get; init; }  = null;
    public IReadOnlyList<string> AllowedValues { get; init; } = [];
}