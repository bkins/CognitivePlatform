namespace CognitivePlatform.Api.Attributes;

[AttributeUsage(AttributeTargets.Parameter
              , AllowMultiple = false)]
public class NaturalLanguageParamAttribute : Attribute
{
    /// <summary>
    /// A natural-language description of what this parameter represents.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// If true, the system treats this parameter as optional during negotiation.
    /// </summary>
    public bool Optional { get; init; }
    
    /// <summary>
    /// Indicates whether the parameter allows an empty string ("") as a valid value.
    /// If false, empty strings will be treated as missing for required parameters.
    /// </summary>
    public bool AllowEmpty { get; init; } = true;
}