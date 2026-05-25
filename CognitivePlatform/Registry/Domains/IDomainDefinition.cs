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
}
