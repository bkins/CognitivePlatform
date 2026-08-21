using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Registry.Capabilities;

/// <summary>
/// Abstract generic base that generates standard CRUD actions for a domain entity.
/// Subclasses supply the domain, entity name, and optional parameter overrides.
///
/// Generated action names follow the convention:
///   Add{EntityName}_Generated, List{EntityNamePlural}_Generated,
///   Get{EntityName}_Generated, Delete{EntityName}_Generated.
///
/// Generated actions are registered under _Generated-suffixed names alongside
/// any existing hand-authored actions — nothing is deleted or renamed.
/// FastPath rules are NOT generated.
/// </summary>
public abstract class CrudCapabilityTemplate<TEntity> : ICapabilityDefinition<TEntity>
    where TEntity : class
{
    public abstract IDomainDefinition Domain     { get; }
    public abstract string            EntityName { get; }

    /// <summary>
    /// Plural form of EntityName used for the List action name.
    /// Defaults to EntityName + "s". Override for irregular plurals (e.g. "JournalEntries").
    /// </summary>
    protected virtual string EntityNamePlural => EntityName + "s";

    /// <summary>
    /// Override to true only for domains that support mutation.
    /// Journal is append-only; SupportsUpdate returns false (the default).
    /// </summary>
    protected virtual bool SupportsUpdate => false;

    public virtual string PromptSummary
        => $"Standard CRUD operations for {EntityName}: Add, List, Get, Delete"
         + (SupportsUpdate ? ", Update." : " (append-only — no Update).");

    /// <summary>
    /// Parameters for the Add action. Subclasses override to declare domain-specific fields.
    /// </summary>
    protected abstract IReadOnlyList<ParameterMetadata> AddParameters();

    public IReadOnlyList<ActionMetadata> BuildActionDefinitions()
    {
        return new List<ActionMetadata>
               {
                   BuildAdd()
                 , BuildList()
                 , BuildGet()
                 , BuildDelete()
               }.AsReadOnly();
    }

    private ActionMetadata BuildAdd()
        => new ActionDefinitionBuilder()
               .Named($"Add{EntityName}_Generated")
               .WithDescription($"Adds a new {EntityName}.")
               .InDomain(Domain)
               .WithParameters(AddParameters())
               .HandledBy(new CrudAddHandler<TEntity>())
               .Build();

    private ActionMetadata BuildList()
        => new ActionDefinitionBuilder()
               .Named($"List{EntityNamePlural}_Generated")
               .WithDescription($"Lists all {EntityNamePlural}.")
               .InDomain(Domain)
               .HandledBy(new CrudListHandler<TEntity>())
               .Build();

    private ActionMetadata BuildGet()
        => new ActionDefinitionBuilder()
               .Named($"Get{EntityName}_Generated")
               .WithDescription($"Gets a {EntityName} by ID.")
               .InDomain(Domain)
               .WithParameter("id"
                            , typeof(string)
                            , $"The ID of the {EntityName} to retrieve."
                            , isOptional: false)
               .HandledBy(new CrudGetHandler<TEntity>())
               .Build();

    private ActionMetadata BuildDelete()
        => new ActionDefinitionBuilder()
               .Named($"Delete{EntityName}_Generated")
               .WithDescription($"Soft-deletes a {EntityName} by ID.")
               .InDomain(Domain)
               .WithParameter("id"
                            , typeof(string)
                            , $"The ID of the {EntityName} to delete."
                            , isOptional: false)
               .HandledBy(new CrudDeleteHandler<TEntity>())
               .Build();
}
