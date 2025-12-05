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
}