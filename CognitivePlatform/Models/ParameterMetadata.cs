using System.Reflection;
using CognitivePlatform.Api.Attributes;

namespace CognitivePlatform.Api.Models;

public class ParameterMetadata
{
    /// <summary>
    /// The name of the parameter (reflection name).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Parameter type information.
    /// </summary>
    public Type ParameterType { get; init; } = typeof(object);

    /// <summary>
    /// Description provided via <see cref="NaturalLanguageParamAttribute"/>.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// True if the caller must provide a value. This should be the logical opposite of IsOptional.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Indicates whether the parameter is optional for the purpose
    /// of conversation negotiation.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Whether empty strings are allowed as a valid value for this parameter.
    /// </summary>
    public bool AllowEmpty { get; init; } = true;
    
    /// <summary>
    /// Default value that will be applied if the parameter is omitted.
    /// May come from the attribute or from the method's default value.
    /// </summary>
    public object? DefaultValue { get; init; }
    
    /// <summary>
    /// Reflection reference to the underlying parameter.
    /// </summary>
    public ParameterInfo? ParameterInfo { get; init; }
}