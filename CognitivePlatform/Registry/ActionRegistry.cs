using System.Reflection;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Registry;

public class ActionRegistry : IActionRegistry
{
    private readonly List<ActionMetadata> _actions = new();
    private readonly List<ActionMetadata> _fastPathActions = new();

    public ActionRegistry ()
    {
        LoadActions();
        //BuildFromAssembly(Assembly.GetExecutingAssembly());
    }

    public IReadOnlyCollection<ActionMetadata> Actions         => _actions;
    public IReadOnlyList<ActionMetadata>       FastPathActions => _fastPathActions;

    public ActionMetadata? FindByName (string name) => _actions.FirstOrDefault(action => string.Equals(action.Name
                                                                                                     , name
                                                                                                     , StringComparison.OrdinalIgnoreCase));

    public void Register(ActionMetadata definition)
    {
        // TODO: When runtime plugin loading is introduced (future phase), this method
        // will need to support post-startup registration. For now, pre-startup only.
        if (_actions.Any(action => string.Equals(action.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"An action named '{definition.Name}' is already registered.");

        _actions.Add(definition);

        if (definition.IsFastPath)
            _fastPathActions.Add(definition);
    }

    private void LoadActions()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var type in assembly.GetTypes())
        {
            // Only consider classes with methods containing [NaturalLanguageAction]
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                              .Where(m => m.GetCustomAttribute<NaturalLanguageActionAttribute>() != null)
                              .ToList();

            if (methods.Any().Not()) continue;

            var domainAttr = type.GetCustomAttribute<DomainAttribute>();
            IDomainDefinition? domain = domainAttr is not null
                                      ? (IDomainDefinition)Activator.CreateInstance(domainAttr.DomainType)!
                                      : null;

            foreach (var method in methods)
            {
                var nla = method.GetCustomAttribute<NaturalLanguageActionAttribute>()!;
                var parameters = method.GetParameters()
                                       .Select(parameter =>
                                       {
                                           var pAttr = parameter.GetCustomAttribute<NaturalLanguageParamAttribute>();
                                           return new ParameterMetadata
                                                  {
                                                          Name          = parameter.Name!
                                                        , ParameterType = parameter.ParameterType
                                                        , Description   = pAttr?.Description ?? string.Empty
                                                        , IsOptional    = pAttr?.Optional    ?? false
                                                        , AllowEmpty    = pAttr?.AllowEmpty  ?? false
                                                        , DefaultValue  = pAttr?.DefaultValue
                                                  };
                                       })
                                       .ToList();

                // Detect [FastPath], [DestructiveAction], and IsReplayable
                var isFastPath    = method.GetCustomAttribute<FastPathAttribute>()        != null;
                var isDestructive = method.GetCustomAttribute<DestructiveActionAttribute>() != null;
                var isReplayable  = nla.IsReplayable;

                var meta = new ActionMetadata
                           {
                                   Name                = method.Name
                                 , Description         = nla.Description
                                 , Category            = nla.Category ?? domain?.Name ?? "General"
                                 , MethodInfo          = method
                                 , Parameters          = parameters
                                 , AllowsClarification = nla.AllowsClarification
                                 , IsFastPath          = isFastPath
                                 , IsDestructive       = isDestructive
                                 , IsReplayable        = isReplayable
                                 , Domain              = domain
                           };

                _actions.Add(meta);

                if (isFastPath) _fastPathActions.Add(meta);
            }
        }
    }

    private void BuildFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public 
                                        | BindingFlags.Instance 
                                        | BindingFlags.Static)
                              .Where(method => method.GetCustomAttribute<NaturalLanguageActionAttribute>() != null)
                              .ToList();
            
            if ( methods.Count == 0)
                continue;
            
            foreach (var method in methods)
            {
                var actionAttribute = method.GetCustomAttribute<NaturalLanguageActionAttribute>();

                if (actionAttribute is null) continue;

                var actionMetadata = BuildActionMetadata(method
                                                       , actionAttribute);

                _actions.Add(actionMetadata);
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
                     , IsReplayable        = attribute.IsReplayable
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
