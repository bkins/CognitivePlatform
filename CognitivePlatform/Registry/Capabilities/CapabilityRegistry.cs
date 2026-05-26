using System.Collections.Concurrent;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Registry.Capabilities;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    // TODO: When runtime plugin loading is introduced (future phase), Register()
    // will need to support post-startup concurrent writes. For now, pre-startup only.
    private readonly IActionRegistry                                _actionRegistry;
    private readonly ConcurrentDictionary<string, ActionMetadata>  _capabilityActions = new(StringComparer.OrdinalIgnoreCase);

    public CapabilityRegistry(IActionRegistry actionRegistry)
    {
        _actionRegistry = actionRegistry;
    }

    public IReadOnlyCollection<ActionMetadata> GetAll()
        => _actionRegistry.Actions
                          .Concat(_capabilityActions.Values)
                          .ToList()
                          .AsReadOnly();

    public bool TryGet(string actionName, out ActionMetadata? definition)
    {
        var fromRegistry = _actionRegistry.FindByName(actionName);
        if (fromRegistry is not null)
        {
            definition = fromRegistry;
            return true;
        }

        return _capabilityActions.TryGetValue(actionName, out definition);
    }

    public void Register(ICapabilityDefinition<object> capability)
    {
        foreach (var actionDefinition in capability.BuildActionDefinitions())
        {
            bool nameAlreadyExists = _actionRegistry.FindByName(actionDefinition.Name) is not null
                                  || !_capabilityActions.TryAdd(actionDefinition.Name, actionDefinition);

            if (nameAlreadyExists)
                throw new InvalidOperationException(
                    $"An action named '{actionDefinition.Name}' is already registered.");
        }
    }
}
