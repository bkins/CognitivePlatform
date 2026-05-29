using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Registry.Capabilities;

public interface ICapabilityRegistry
{
    /// <summary>
    /// All actions from all sources: reflected methods + Phase 2 registered + capability-registered.
    /// This is the combined catalog.
    /// </summary>
    IReadOnlyCollection<ActionMetadata> GetAll();

    bool TryGet(string actionName, out ActionMetadata? definition);

    /// <summary>
    /// Register a capability's generated actions into the combined catalog.
    /// Must be called before the application starts handling requests.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if any action produced by the capability has a name already registered.
    /// </exception>
    void Register(ICapabilityDefinition<object> capability);

    /// <summary>
    /// Returns the aggregated <see cref="ICapabilityDefinition{T}.PromptSummary"/> text
    /// for all capabilities registered under the given domain, joined by "; ".
    /// Returns <c>null</c> when no capability has been registered for that domain —
    /// the caller should fall back to <see cref="IDomainDefinition.Description"/>.
    /// </summary>
    string? GetDomainPromptSummary(string domainName);
}
