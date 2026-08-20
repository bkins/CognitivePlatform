using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Workspace;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Journal;

public class JournalRevisionRepository : IJournalRevisionRepository
{
    private readonly IObjectStore      _store;
    private readonly IWorkspaceContext? _workspaceContext;

    public JournalRevisionRepository(IObjectStore store, IWorkspaceContext? workspaceContext = null)
    {
        _store            = store;
        _workspaceContext = workspaceContext;
    }

    public IReadOnlyList<JournalRevision>
            GetRevisionsByEntryId(string entryId)
    {
        if (entryId.HasNoValue())
            throw new ArgumentException("entryId cannot be null or empty.",
                                        nameof(entryId));

        var partitionKey = _workspaceContext?.ActivePartitionKey;
        var revisions = _store.List<JournalRevision>(partitionKey);

        if (revisions.Count == 0 && partitionKey != null)
        {
            revisions = _store.List<JournalRevision>(null);
        }

        return revisions.Where(revision => revision.EntryId == entryId)
                        .OrderByDescending(revision => revision.CreatedUtc)
                        .ToList();
    }
}