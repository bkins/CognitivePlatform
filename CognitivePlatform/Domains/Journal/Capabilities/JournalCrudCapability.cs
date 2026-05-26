using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Journal.Capabilities;

/// <summary>
/// Generates standard CRUD actions for the Journal domain.
/// Entity type is JournalEntryWithRevision — the natural full-entity view
/// since JournalEntry alone carries no text (text lives in JournalRevision).
///
/// Generated actions are registered under _Generated-suffixed names alongside
/// the existing JournalActions methods. Journal is append-only, so SupportsUpdate
/// remains false (the default).
/// </summary>
public sealed class JournalCrudCapability : CrudCapabilityTemplate<JournalEntryWithRevision>
{
    public override IDomainDefinition Domain          => new JournalDomain();
    public override string            EntityName      => "JournalEntry";
    protected override string         EntityNamePlural => "JournalEntries";

    // Journal is append-only — SupportsUpdate stays false (inherited default).

    protected override IReadOnlyList<ParameterMetadata> AddParameters()
    {
        return new List<ParameterMetadata>
               {
                   new ParameterMetadata
                   {
                           Name          = "text"
                         , ParameterType = typeof(string)
                         , Description   = "The text content of the journal entry."
                         , IsOptional    = false
                   }
                 , new ParameterMetadata
                   {
                           Name          = "mood"
                         , ParameterType = typeof(string)
                         , Description   = "Optional free-form mood description (e.g. 'anxious but hopeful')."
                         , IsOptional    = true
                   }
               }.AsReadOnly();
    }
}
