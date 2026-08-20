using System.Reflection;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Registry;

public class ActionRegistry : IActionRegistry
{
    private readonly List<ActionMetadata> _actions = new();
    private readonly List<ActionMetadata> _fastPathActions = new();

    public IReadOnlyCollection<ActionMetadata> Actions                  => _actions;
    public IReadOnlyList<ActionMetadata>       FastPathActions          => _fastPathActions;
    public ActionMetadata?                     FindByName (string name) => _actions.FirstOrDefault(action => action.Name.EqualsIgnoreCase(name));
    public ActionRegistry ()
    {
        LoadActions();
        //BuildFromAssembly(Assembly.GetExecutingAssembly());
    }

    public void Register(ActionMetadata definition)
    {
        //TODO: When runtime plugin loading is introduced (future phase), this method
        // will need to support post-startup registration. For now, pre-startup only.
        if (_actions.Any(action => action.Name.EqualsIgnoreCase(definition.Name)))
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
            var methods = type.GetMethods(BindingFlags.Public 
                                        | BindingFlags.Instance 
                                        | BindingFlags.Static)
                              .Where(method => method.GetCustomAttribute<NaturalLanguageActionAttribute>() != null)
                              .ToList();

            if (methods.Count == 0) continue;

            AddMetadata(methods, type);
        }
    }

    private void AddMetadata( List<MethodInfo>   methods
                            , Type               type )   
    {
        var domainAttr = type.GetCustomAttribute<DomainAttribute>();
        var domain = domainAttr is not null
                             ? (IDomainDefinition)Activator.CreateInstance(domainAttr.DomainType)!
                             : null;

        foreach (var method in methods)
        {
            var actionAttribute = method.GetCustomAttribute<NaturalLanguageActionAttribute>()!;
            var parameters      = BuildActionParameters(method);
            var isFastPath      = method.GetCustomAttribute<FastPathAttribute>() != null;
                
            var metadata = BuildActionMetadata(method
                                             , actionAttribute
                                             , domain
                                             , parameters
                                             , isFastPath);

            _actions.Add(metadata);

            if (isFastPath) _fastPathActions.Add(metadata);
        }
    }

    private static ActionMetadata BuildActionMetadata( MethodInfo                     method
                                                     , NaturalLanguageActionAttribute actionAttribute
                                                     , IDomainDefinition?             domain
                                                     , List<ParameterMetadata>        parameters
                                                     , bool                           isFastPath)
    {
        return new ActionMetadata
               {
                       Name                = method.Name
                     , Description         = actionAttribute.Description
                     , Category            = actionAttribute.Category ?? domain?.Name ?? "General"
                     , MethodInfo          = method
                     , Parameters          = parameters
                     , AllowsClarification = actionAttribute.AllowsClarification
                     , IsFastPath          = isFastPath
                     , IsDestructive       = method.GetCustomAttribute<DestructiveActionAttribute>() != null
                     , IsReplayable        = actionAttribute.IsReplayable
                     , Examples            = actionAttribute.Examples
                     , Domain              = domain
               };
    }

    private static List<ParameterMetadata> BuildActionParameters( MethodInfo method )
    {
        return method.GetParameters()
                     .Select(parameter =>
                      {
                          var attribute = parameter.GetCustomAttribute<NaturalLanguageParamAttribute>();
                          return new ParameterMetadata
                                 {
                                         Name          = parameter.Name!
                                       , ParameterType = parameter.ParameterType
                                       , Description   = attribute?.Description ?? string.Empty
                                       , IsOptional    = attribute?.Optional    ?? false
                                       , AllowEmpty    = attribute?.AllowEmpty  ?? false
                                       , DefaultValue  = attribute?.DefaultValue
                                 };
                      })
                     .ToList();
    }
}
