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

    /// <summary>
    /// Registers a programmatically-defined action. Must be called before the application
    /// starts handling requests (pre-startup only).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if an action with the same name is already registered.
    /// </exception>
    void Register(ActionMetadata definition);
}