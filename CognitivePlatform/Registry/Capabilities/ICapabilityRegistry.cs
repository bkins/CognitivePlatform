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
}
