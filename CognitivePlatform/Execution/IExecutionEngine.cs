using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Execution;

public interface IExecutionEngine
{
    /// <summary>
    /// Executes the given action asynchronously and returns a human-readable result.
    /// </summary>
    Task<string> ExecuteAsync( ActionMetadata              action
                             , IDictionary<string, string> arguments
                             , string                      sessionId
                             , CancellationToken           ct = default );
}
