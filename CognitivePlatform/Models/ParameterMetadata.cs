using System.Reflection;

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
    /// Indicates whether the parameter is optional for the purpose
    /// of conversation negotiation.
    /// </summary>
    public bool Optional { get; init; }

    /// <summary>
    /// Reflection reference to the underlying parameter.
    /// </summary>
    public ParameterInfo? ParameterInfo { get; init; }
}