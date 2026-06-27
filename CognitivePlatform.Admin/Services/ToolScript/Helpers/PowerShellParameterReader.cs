using System.Management.Automation.Language;
using CognitivePlatform.Admin.Services.ToolScript.Interfaces;
using CognitivePlatform.Admin.Services.ToolScript.Models;
using CP.Client.Core.Avails;

namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

public sealed class PowerShellParameterReader : IPowerShellParameterReader
{
    public IReadOnlyList<ToolParameter> Read( string scriptPath )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        Token[]      tokens;
        ParseError[] errors;

        var ast = Parser.ParseFile(scriptPath
                                 , out tokens
                                 , out errors);

        if (errors.Length > 0)
        {
            throw new InvalidOperationException($"Failed to parse '{scriptPath}': {errors[0].Message}");
        }
        var scriptText = File.ReadAllText(scriptPath);
        
        return ast.ParamBlock is null
                       ? []
                       : ast.ParamBlock
                            .Parameters
                            .Select(parameter => BuildParameter(parameter
                                                               , scriptText)).ToList();

    }

    private static ToolParameter BuildParameter( ParameterAst parameter
                                               , string       scriptText )
    {
        var type = parameter.Attributes
                            .OfType<TypeConstraintAst>()
                            .FirstOrDefault()
                           ?.TypeName.FullName
                ?? "object";

        var mandatory     = false;
        var allowedValues = new List<string>();

        foreach (var attribute in parameter.Attributes.OfType<AttributeAst>())
        {
            var attributeName = attribute.TypeName.Name;

            if (attributeName.Equals("Parameter"
                                   , StringComparison.OrdinalIgnoreCase))
            {
                foreach (var namedArgument in attribute.NamedArguments)
                {
                    if (namedArgument.ArgumentName.Equals("Mandatory"
                                                        , StringComparison.OrdinalIgnoreCase))
                    {
                        mandatory = true;
                    }
                }
            }

            if (attributeName.Equals("ValidateSet"
                                    , StringComparison.OrdinalIgnoreCase)
                             .Not()) continue;
            
            foreach (var value in attribute.PositionalArguments)
            {
                if (value.SafeGetValue() is { } safeValue)
                {
                    allowedValues.Add(safeValue.ToString()!);
                }
            }
        }

        var label = PowerShellCommentReader.ReadLabel(scriptText
                                                    , parameter)
                 ?? Humanize(parameter.Name.VariablePath.UserPath);
        var paramType = MapType(type, allowedValues);
        
        return new ToolParameter
               {
                       Name          = parameter.Name.VariablePath.UserPath
                     , Label         = label
                     , ParameterType = paramType
                     , IsMandatory   = mandatory
                     , DefaultValue  = ParseDefaultValue(parameter.DefaultValue, paramType)
                     , AllowedValues = allowedValues
               };
    }

    private static object? ParseDefaultValue( ExpressionAst?    expression
                                            , ToolParameterType parameterType )
    {
        if (expression is null)
            return null;

        var text = expression.Extent.Text.Trim();

        return parameterType switch
        {
                ToolParameterType.Boolean =>
                        text.Equals("$true", StringComparison.OrdinalIgnoreCase)
                        
              , ToolParameterType.Number =>
                        int.TryParse(text, out var i)
                                ? i
                                : text
                                
              , _ => text.Trim('"')
        };
    }

    private static ToolParameterType MapType( string                      powerShellType
                                            , IReadOnlyCollection<string> allowedValues )
    {
        if (allowedValues.Count > 0) return ToolParameterType.Selection;

        return powerShellType.ToLowerInvariant() switch
        {
                "switch"  => ToolParameterType.Boolean
              , "bool"    => ToolParameterType.Boolean
              , "boolean" => ToolParameterType.Boolean
              , "int"     => ToolParameterType.Number
              , "int32"   => ToolParameterType.Number
              , "int64"   => ToolParameterType.Number
              , "double"  => ToolParameterType.Number
              , "decimal" => ToolParameterType.Number
              , _         => ToolParameterType.Text
        };
    }

    //TODO: have this class look for the comments above each parament, like `# @Label Root Path`, instead of the `Humanize` method
    private static string Humanize( string name )
    {
        return System.Text.RegularExpressions.Regex.Replace(name
                                                          , "([a-z])([A-Z])"
                                                          , "$1 $2");
    }
}