using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Registry;

public interface IActionRegistry
{
    /// <summary>
    /// All discovered actions, built at startup.
    /// </summary>
    IReadOnlyCollection<ActionMetadata> Actions { get; }

    /// <summary>
    /// Finds a single action by its name, or null if not found.
    /// </summary>
    ActionMetadata? FindByName(string name);
    
    IReadOnlyList<ActionMetadata> FastPathActions { get; }

}