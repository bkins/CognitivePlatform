using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Registry.Capabilities;

/// <summary>
/// Defines the set of capabilities supported by a domain entity type T.
/// Produces ActionMetadata instances consumed by the CapabilityRegistry.
/// </summary>
public interface ICapabilityDefinition<out T> where T : class
{
    IDomainDefinition Domain { get; }

    /// <summary>
    /// A short summary of this capability's actions, used by the LLM interpreter
    /// for domain-aware prompt partitioning. When the interpreter is operating in
    /// domain-scoped mode, this summary may replace individual action enumeration.
    /// </summary>
    string PromptSummary { get; }

    IReadOnlyList<ActionMetadata> BuildActionDefinitions();
}
