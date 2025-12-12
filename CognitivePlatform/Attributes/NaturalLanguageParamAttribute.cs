namespace CognitivePlatform.Api.Attributes;

[AttributeUsage(AttributeTargets.Parameter
              , AllowMultiple = false)]
public class NaturalLanguageParamAttribute : Attribute
{
    public string? Name { get; set; }
    /// <summary>
    /// A natural-language description of what this parameter represents.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// True if the parameter may be omitted. This will map to ParameterMetadata.IsOptional.
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    /// If true, the system treats this parameter as optional during negotiation.
    /// </summary>
    public bool Optional 
    {
        get => IsOptional;
        set => IsOptional = value;
    }
    
    /// <summary>
    /// Indicates whether the parameter allows an empty string ("") as a valid value.
    /// If false, empty strings will be treated as missing for required parameters.
    /// </summary>
    public bool AllowEmpty { get; init; } = true;
    
    /// <summary>
    /// Default value to use if the parameter is omitted entirely.
    /// If null, we may fall back to the method's reflection default.
    /// </summary>
    public object? DefaultValue { get; set; }
}