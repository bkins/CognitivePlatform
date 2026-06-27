namespace CognitivePlatform.Admin.Services.ToolScript.Interfaces;

public interface IToolScriptRunner
{
    Task<int?> RunAsync( string                               terminalId
                       , Models.ToolScript                    tool
                       , IReadOnlyDictionary<string, object?> values
                       , CancellationToken                    cancellationToken = default );
}