using System.Collections.Concurrent;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Registry.Capabilities;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    // TODO: When runtime plugin loading is introduced (future phase), Register()
    // will need to support post-startup concurrent writes. For now, pre-startup only.
    private readonly IActionRegistry                               _actionRegistry;
    private readonly ConcurrentDictionary<string, ActionMetadata> _capabilityActions  = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aggregated PromptSummary strings per domain name, built up during Register().
    /// A domain may have multiple capabilities (e.g. JournalCrud + JournalSummary);
    /// each capability's summary is appended with "; " so callers get the full picture.
    /// </summary>
    private readonly ConcurrentDictionary<string, List<string>> _domainSummaries = new(StringComparer.OrdinalIgnoreCase);

    public CapabilityRegistry(IActionRegistry actionRegistry)
    {
        _actionRegistry = actionRegistry;
    }

    public IReadOnlyCollection<ActionMetadata> GetAll() => _actionRegistry.Actions
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

        var domainName = capability.Domain.Name;
        var summaries  = _domainSummaries.GetOrAdd(domainName, _ => new List<string>());

        lock (summaries)
            summaries.Add(capability.PromptSummary);
    }

    public string? GetDomainPromptSummary(string domainName)
    {
        if (!_domainSummaries.TryGetValue(domainName, out var summaries))
            return null;

        lock (summaries)
            return summaries.Count == 0 ? null : string.Join("; ", summaries);
    }
}
