using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Registry;

public sealed class ActionDefinitionBuilder
{
    private string?                  _name;
    private string                   _description      = string.Empty;
    private IDomainDefinition?       _domain;
    private string?                  _category;
    private string[]?                _examples;
    private readonly List<ParameterMetadata> _parameters = new();
    private bool                     _allowsClarification;
    private bool                     _isReplayable;
    private bool                     _isFastPath;
    private IActionHandler?          _handler;

    public ActionDefinitionBuilder Named(string name)
    {
        _name = name;
        return this;
    }

    public ActionDefinitionBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ActionDefinitionBuilder InDomain(IDomainDefinition domain)
    {
        _domain   = domain;
        _category ??= domain.Name;
        return this;
    }

    public ActionDefinitionBuilder WithExamples(params string[] examples)
    {
        _examples = examples;
        return this;
    }

    public ActionDefinitionBuilder WithParameter( string  parameterName
                                                , Type    parameterType
                                                , string  description
                                                , bool    isOptional   = false
                                                , object? defaultValue = null )
    {
        _parameters.Add(new ParameterMetadata
                        {
                                Name          = parameterName
                              , ParameterType = parameterType
                              , Description   = description
                              , IsOptional    = isOptional
                              , DefaultValue  = defaultValue
                        });
        return this;
    }

    public ActionDefinitionBuilder AllowsClarification()
    {
        _allowsClarification = true;
        return this;
    }

    public ActionDefinitionBuilder AsReplayable()
    {
        _isReplayable = true;
        return this;
    }

    public ActionDefinitionBuilder AsFastPath()
    {
        _isFastPath = true;
        return this;
    }

    public ActionDefinitionBuilder HandledBy(IActionHandler handler)
    {
        _handler = handler;
        return this;
    }

    public ActionMetadata Build()
    {
        if (string.IsNullOrWhiteSpace(_name))
            throw new InvalidOperationException(
                "Action name must be set via Named() before calling Build().");

        return new ActionMetadata
               {
                       Name                = _name
                     , Description         = _description
                     , Domain              = _domain
                     , Category            = _category ?? _domain?.Name ?? "General"
                     , Examples            = _examples?.ToArray()
                     , Parameters          = _parameters.ToList()
                     , AllowsClarification = _allowsClarification
                     , IsReplayable        = _isReplayable
                     , IsFastPath          = _isFastPath
                     , Handler             = _handler
               };
    }
}
