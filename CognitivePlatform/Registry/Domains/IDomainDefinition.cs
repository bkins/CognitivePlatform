namespace CognitivePlatform.Api.Registry.Domains;

/// <summary>
/// Represents a first-class domain boundary in the platform.
/// Domains are the units of capability loading, permission scoping,
/// prompt partitioning, telemetry aggregation, and plugin isolation.
/// </summary>
public interface IDomainDefinition
{
    /// <summary>
    /// Canonical name used in action categories, telemetry, and prompt context.
    /// Must be stable across versions — it becomes part of the persisted audit trail.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description for admin UI and diagnostics.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Natural-language keywords used by the LLM interpreter's domain-relevance scorer.
    /// When any keyword appears in the user's input, this domain's actions are included
    /// in full in the system prompt; otherwise only a compact summary is emitted.
    /// Default is empty — an empty list means the domain always falls back to summary mode
    /// unless another mechanism marks it relevant.
    /// </summary>
    IReadOnlyList<string> Keywords => Array.Empty<string>();
}
