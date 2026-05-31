using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Api.Domains.Journal;
/// KNOWLEDGE SOURCE (ADAPTER)
/// -------------------------
/// Adapts a domain into the Knowledge system.
/// - Translates domain objects into KnowledgeItemDto.
/// - Implements knowledge lifecycle actions (archive, hide, etc.).
/// - May call domain services or persistence, but owns no business rules.
///
/// Rule of thumb:
/// If the Knowledge system disappeared tomorrow,
/// this class should be deleted.

/// <summary>
/// Knowledge source for journal entries. Adapts journal domain objects
/// </summary>
public sealed class JournalKnowledgeSource : IKnowledgeSource
{
    private readonly IJournalService _journalService;
    private readonly IObjectStore    _objectStore;

    public KnowledgeKind Kind => KnowledgeKind.Journal;

    public JournalKnowledgeSource (IJournalService journalService
                                 , IObjectStore    objectStore)
    {
        _journalService = journalService;
        _objectStore    = objectStore;
    }

    public IEnumerable<KnowledgeItemDto> GetKnowledgeItems (KnowledgeQuery    query
                                                          , CancellationToken ct)
    {
        // TODO: introduce filtering
        // NOTE: Filtering is intentionally deferred to the aggregator for now
        
        var entryWithRevisions = _journalService.ListAllEntries();
        
        foreach (var entryWithRevision in entryWithRevisions)
        {
            if (query.Id is not null 
             && entryWithRevision.Entry.Id != query.Id.Value.ToString("N"))
                continue;
            
            IReadOnlyList<string> tags = entryWithRevision.LatestRevision.Tags is { Count: > 0 }
                                                         ? entryWithRevision.LatestRevision.Tags
                                                         : Array.Empty<string>();
            yield return new KnowledgeItemDto
                         {
                                 Id             = Guid.Parse(entryWithRevision.Entry.Id)
                               , Kind           = KnowledgeKind.Journal
                               , Title          = DeriveTitle(entryWithRevision.LatestRevision)
                               , Summary        = DeriveSummary(entryWithRevision.LatestRevision)
                               , CreatedAt      = entryWithRevision.Entry.CreatedUtc
                               , LastModifiedAt = entryWithRevision.Entry.CreatedUtc // TODO: update when journal edits are introduced
                               , Status         = KnowledgeStatus.Active
                               , Tags           = tags
                               , IsEdited       = entryWithRevision.IsEdited
                               , Importance     = null
                               , Urgency        = null
                         };
        }
    }

    public IReadOnlyList<ObjectHeader> ListHeaders (DateTimeOffset? fromUtc
                                                   , DateTimeOffset? toUtc)
    {
        return _journalService.ListAllEntries(fromUtc, toUtc)
                              .Select(entryWithRevision => new ObjectHeader(entryWithRevision.Entry.Id
                                                                          , nameof(KnowledgeKind.Journal)
                                                                          , entryWithRevision.Entry.CreatedUtc
                                                                          , entryWithRevision.LatestRevision.CreatedUtc))
                              .ToList();
    }

    public void Archive(Guid id, CancellationToken ct)
    {
        // NOTE: For journals, "archive" is currently implemented
        // as a soft delete. This may change in the future.
        _objectStore.SoftDelete<JournalEntry>(id.ToString("N"));
    }

    private static string DeriveTitle (JournalRevision entry)
    {
        return entry.Text
                    .Length <= 60
                       ? entry.Text
                       : string.Concat(entry.Text.AsSpan(0, 57), "…");
    }

    private static string? DeriveSummary (JournalRevision entry)
    {
        return entry.Text
                    .Length <= 140
                       ? null
                       : string.Concat(entry.Text.AsSpan(0, 137), "…");
    }
}
