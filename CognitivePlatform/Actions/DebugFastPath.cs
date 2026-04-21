using System.ComponentModel;
using System.Linq;
using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Api.Actions;

[Category("debug")]
public sealed class DebugFastPath
{
    private readonly IActionRegistry _registry;

    public DebugFastPath( IActionRegistry registry )
    {
        _registry = registry;
    }

    [NaturalLanguageAction(Description = "Shows diagnostic information about all FastPath-enabled actions."
                         , Examples = new[]
                                      {
                                              "Show fast path diagnostics."
                                            , "Debug fast path actions."
                                            , "What actions support fast-path?"
                                      }
                         , AllowsClarification = false
                         , Category = "debug")]
    public string ShowFastPathDiagnostics()
    {
        var sb = new StringBuilder();

        sb.AppendLine("FAST PATH DIAGNOSTICS");
        sb.AppendLine("----------------------");

        var fast = _registry.FastPathActions;

        if (fast.Count == 0)
        {
            sb.AppendLine("No actions marked with [FastPath].");
            return sb.ToString();
        }

        foreach (var action in fast)
        {
            sb.AppendLine();
            sb.AppendLine($"Action: {action.Name}");
            sb.AppendLine($"Category: {action.Category}");
            sb.AppendLine($"Description: {action.Description}");

            var required = action.Parameters
                                 .Where(parameter => parameter.IsOptional
                                                              .Not())
                                 .ToList();

            var optional = action.Parameters
                                 .Where(parameter => parameter.IsOptional)
                                 .ToList();

            sb.Append("Required Parameters: ");
            sb.AppendLine(required.Count == 0
                                  ? "(none)"
                                  : string.Join(", "
                                              , required.Select(r => $"{r.Name}:{r.ParameterType.Name}")));

            sb.Append("Optional Parameters: ");
            sb.AppendLine(optional.Count == 0
                                  ? "(none)"
                                  : string.Join(", "
                                              , optional.Select(o => $"{o.Name}:{o.ParameterType.Name}")));

            // Diagnostic rule: FastPathResolver only considers actions with exactly 1 required param.
            if (required.Count == 1)
            {
                sb.AppendLine("FastPath Status: VALID — eligible for fast-path inference.");
            }
            else
            {
                sb.AppendLine("FastPath Status: WARNING — not eligible for fast-path (must have exactly one required parameter).");
            }
        }

        return sb.ToString();
    }
}
