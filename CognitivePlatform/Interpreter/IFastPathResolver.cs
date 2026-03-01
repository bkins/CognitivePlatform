using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Interpreter;

public interface IFastPathResolver
{
    bool TryResolve( string                          input
                   , out ActionMetadata?             action
                   , out Dictionary<string, string>? parameters );
}