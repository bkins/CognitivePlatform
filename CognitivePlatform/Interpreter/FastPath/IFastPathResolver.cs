using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Interpreter;

public interface IFastPathResolver
{
    bool TryResolve( string                           input
                   , out ActionMetadata?             action
                   , out Dictionary<string, string>? parameters );

    /// <summary>
    /// Checks whether <paramref name="input"/> begins with a workspace-switching prefix
    /// of the form <c>"workspaceName: remainder"</c> (e.g. <c>"work: add task X"</c>).
    ///
    /// Returns <c>true</c> and sets <paramref name="workspaceName"/> / <paramref name="remainder"/>
    /// when a workspace prefix is detected.  Returns <c>false</c> for all built-in reserved
    /// prefixes: daily-record commands (Plan:, Check:, EOD:, Done:, …) and registered action names.
    /// </summary>
    bool TryExtractWorkspacePrefix( string     input
                                  , out string? workspaceName
                                  , out string? remainder );
}
