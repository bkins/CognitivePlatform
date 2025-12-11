using System.Reflection;

namespace CognitivePlatform.Api.Models;

public class ActionMetadata
{
    /// <summary>
    /// The developer-facing name of the method.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Reflection reference to the target method.
    /// </summary>
    public MethodInfo? MethodInfo { get; init; }

    /// <summary>
    /// A human-friendly description of what this action does.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Example natural language phrases that invoke this action.
    /// </summary>
    public string[]? Examples { get; init; }

    /// <summary>
    /// List of parameter metadata extracted from the method.
    /// </summary>
    public List<ParameterMetadata> Parameters { get; init; } = new();
    
    /// <summary>
    /// Canonical, normalized category (PascalCase).
    /// Guaranteed to be non-null. Defaults to "General".
    /// </summary>
    public string Category { get; init; } = "General";
    
    /// <summary>
    /// Whether this action is allowed to trigger clarifying questions
    /// when required parameters are missing.
    /// </summary>
    public bool AllowsClarification { get; init; }
}