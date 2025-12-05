using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Execution;

public interface IExecutionEngine
{
    /// <summary>
    /// Executes the given action and returns a human-readable result.
    /// Phase 1: no parameter binding, no DI, no safety.
    /// </summary>
    string Execute(ActionMetadata action, IReadOnlyDictionary<string, string> arguments);
}