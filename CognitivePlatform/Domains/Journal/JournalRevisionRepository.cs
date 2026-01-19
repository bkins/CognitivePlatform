using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Journal;

public class JournalRevisionRepository : IJournalRevisionRepository
{
    private readonly IObjectStore _store;

    public JournalRevisionRepository(IObjectStore store)
    {
        _store = store;
    }

    public IReadOnlyList<JournalRevision>
            GetRevisionsByEntryId(string entryId)
    {
        if (entryId.HasNoValue())
            throw new ArgumentException("entryId cannot be null or empty.",
                                        nameof(entryId));

        // ObjectStore is dumb; domain owns meaning
        var revisions = _store.List<JournalRevision>();

        return revisions.Where(revision => revision.EntryId == entryId)
                        .OrderByDescending(revision => revision.CreatedUtc)
                        .ToList();
    }
}