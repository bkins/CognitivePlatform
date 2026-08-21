namespace CognitivePlatform.Api.Registry;

public sealed class ParameterDefinition(string name, string displayName, Type type)
{
    public string Name        { get; init; } = name;
    public string DisplayName { get; init; } = $"{displayName} ({name})";
    public Type   Type        { get; init; } = type;
    public bool   IsRequired  { get; set; }

    public int?    MaxLength    { get; set; }
    public bool    IsUnicode    { get; set; } = true;
    public object? DefaultValue { get; set; }

    public List<string> ValidationRules { get; set; } = new();
}
