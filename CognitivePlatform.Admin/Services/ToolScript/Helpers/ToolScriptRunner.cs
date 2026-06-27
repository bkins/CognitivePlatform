using System.Diagnostics;
using System.Text;
using CognitivePlatform.Admin.Services.ToolScript.Interfaces;
using CognitivePlatform.Admin.Services.ToolScript.Models;
using CP.Client.Core.Avails;

namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

public sealed class ToolScriptRunner : IToolScriptRunner
{
    private readonly ITerminalStateService _terminal;

    public ToolScriptRunner( ITerminalStateService terminal )
    {
        _terminal = terminal;
    }

    public async Task<int?> RunAsync( string                               terminalId
                                    , Models.ToolScript                    tool
                                    , IReadOnlyDictionary<string, object?> values
                                    , CancellationToken                    cancellationToken = default )
    {
        var arguments = BuildArguments(tool, values);

        var psi = new ProcessStartInfo
                  {
                          FileName               = "pwsh.exe"
                        , Arguments              = $"-NoProfile -ExecutionPolicy Bypass -File \"{tool.ScriptPath}\" {arguments}"
                        , RedirectStandardOutput = true
                        , RedirectStandardError  = true
                        , UseShellExecute        = false
                        , CreateNoWindow         = true
                        , StandardOutputEncoding = Encoding.UTF8
                        , StandardErrorEncoding  = Encoding.UTF8
                  };

        _terminal.Clear(terminalId);
        _terminal.MarkRunning(terminalId
                            , true);

        var exitCode = await _terminal.RunProcessAsync(terminalId
                                                     , psi
                                                     , ct: cancellationToken);

        _terminal.SetExitCode(terminalId, exitCode);
        _terminal.MarkRunning(terminalId, false);

        return exitCode;
    }

    private static string BuildArguments( Models.ToolScript                    tool
                                        , IReadOnlyDictionary<string, object?> values )
    {
        var builder = new List<string>();

        foreach (var parameter in tool.Parameters)
        {
            if (values.TryGetValue(parameter.Name, out var value).Not()
             || value is null)
            {
                continue;
            }

            switch (parameter.ParameterType)
            {
                case ToolParameterType.Boolean:
                    if (value is bool b && b)
                    {
                        builder.Add($"-{parameter.Name}");
                    }

                    break;

                case ToolParameterType.Text:
                case ToolParameterType.Number:
                case ToolParameterType.Selection:
                default:
                    builder.Add($"-{parameter.Name}");
                    builder.Add($"\"{value}\"");
                    break;
            }
        }

        return string.Join(" ", builder);
    }
}