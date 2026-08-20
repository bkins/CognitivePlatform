using System.Management.Automation.Language;
using CP.Client.Core.Avails;

namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

internal static class PowerShellCommentReader
{
    public static string? ReadLabel( string       scriptText
                                   , ParameterAst parameter )
    {
        // Everything before this parameter
        var before = scriptText[..parameter.Extent.StartOffset];

        // Walk backwards looking for "# @Label ..."
        var lines = before.Split(["\r\n", "\n"]
                               , StringSplitOptions.None);

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();

            if (line.HasNoValue())
                continue;

            if (line.StartsWith("# @Label "
                              , StringComparison.Ordinal))
            {
                return line["# @Label ".Length..].Trim();
            }

            // Stop once we hit a non-comment
            if (line.StartsWith("#").Not())
                break;
        }

        return null;
    }
}