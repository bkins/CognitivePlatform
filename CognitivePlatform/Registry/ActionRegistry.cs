using System.Reflection;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Registry;

public class ActionRegistry : IActionRegistry
{
    private readonly List<ActionMetadata> _actions = new();
    private readonly ITelemetrySink       _telemetry;

    public ActionRegistry (ITelemetrySink   telemetry)
    {
        _telemetry = telemetry;
        
        BuildFromAssembly(Assembly.GetExecutingAssembly());
    }

    public IReadOnlyCollection<ActionMetadata> Actions => _actions;

    public ActionMetadata? FindByName (string name) => _actions.FirstOrDefault(action => string.Equals(action.Name
                                                                                                     , name
                                                                                                     , StringComparison.OrdinalIgnoreCase));

    private void BuildFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance
                                                 | BindingFlags.Static
                                                 | BindingFlags.Public
                                                 | BindingFlags.DeclaredOnly))
            {
                var actionAttribute = method.GetCustomAttribute<NaturalLanguageActionAttribute>();

                if (actionAttribute is null) continue;

                var actionMetadata = BuildActionMetadata(method
                                                       , actionAttribute);

                _actions.Add(actionMetadata);
                
                _telemetry.Track("Registry.ActionDiscovered", actionMetadata.Name);
            }
        }
    }

    private static ActionMetadata BuildActionMetadata (MethodInfo                     methodInfo
                                                     , NaturalLanguageActionAttribute attribute)
    {
        var parameters = methodInfo.GetParameters()
                                   .Select(BuildParameterMetadata)
                                   .ToList();
        
        var rawCategory = attribute.Category;

        // Step 1: default if null or whitespace
        var normalized = string.IsNullOrWhiteSpace(rawCategory)
                            ? "general"
                            : rawCategory.Trim().ToLowerInvariant();

        // Step 2: collapse spaces (e.g. "memory tools" → "memorytools")
        normalized = string.Concat(normalized.Where(char.IsLetterOrDigit));
        
        // Step 3: PascalCase it for internal use and display
        if (normalized.Length == 0) normalized = "general";

        var category = char.ToUpper(normalized[0]) + normalized[1..];

        return new ActionMetadata
               {
                       Name                = methodInfo.Name
                     , MethodInfo          = methodInfo
                     , Description         = attribute.Description
                     , Examples            = attribute.Examples
                     , Parameters          = parameters
                     , Category            = category
                     , AllowsClarification = attribute.AllowsClarification
               };
    }

    private static ParameterMetadata BuildParameterMetadata(ParameterInfo parameterInfo)
    {
        var nlAttribute = parameterInfo.GetCustomAttribute<NaturalLanguageParamAttribute>();
        
        // Determine optionality:
        // - Attribute.Optional wins if set
        // - Otherwise fall back to reflection (IsOptional / HasDefaultValue)
        var isOptional = nlAttribute?.Optional
                      ?? parameterInfo.IsOptional
                      || parameterInfo.HasDefaultValue;
        
        // Determine default value:
        // - Attribute.DefaultValue wins if set (including "null" as an intentional choice)
        // - Otherwise, use reflection default if present
        var defaultValue = nlAttribute?.DefaultValue;
        
        if (defaultValue is null 
         && parameterInfo.HasDefaultValue)
        {
            defaultValue = parameterInfo.DefaultValue;
        }

        return new ParameterMetadata
               {
                       Name          = parameterInfo.Name ?? string.Empty
                     , ParameterType = parameterInfo.ParameterType
                     , Description   = nlAttribute?.Description ?? string.Empty
                     , IsOptional    = isOptional
                     , AllowEmpty    = nlAttribute?.AllowEmpty ?? true
                     , ParameterInfo = parameterInfo
                     , DefaultValue  = defaultValue
               };
    }
}
