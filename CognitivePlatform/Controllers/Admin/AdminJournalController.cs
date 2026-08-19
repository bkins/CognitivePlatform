using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/journal")]
public sealed class AdminJournalController : AdminControllerBase
{
    private readonly SqliteObjectStore          _store;
    private readonly IJournalRevisionRepository _revisions;

    public AdminJournalController( IConfiguration              configuration
                                 , SqliteObjectStore            store
                                 , IJournalRevisionRepository   revisions)
        : base(configuration)
    {
        _store     = store;
        _revisions = revisions;
    }

    /// <summary>
    /// Returns all journal entries (including soft-deleted), newest first.
    /// Each entry includes a text excerpt and count from its latest revision.
    /// Fetches all revisions in one pass to avoid N+1 queries.
    /// </summary>
    [HttpGet("entries")]
    public IActionResult GetEntries()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entries      = _store.ListIncludingDeleted<JournalEntry>();
        var allRevisions = _store.List<JournalRevision>();

        var latestByEntry = allRevisions.GroupBy(revision => revision.EntryId)
                                        .ToDictionary(group => group.Key
                                                    , group => group.OrderByDescending(revision => revision.CreatedUtc)
                                                                    .FirstOrDefault());

        var revisionCountByEntry = allRevisions.GroupBy(revision => revision.EntryId)
                                               .ToDictionary(group => group.Key
                                                           , group => group.Count());

        var result = entries.Select(entry =>
                             {
                                 latestByEntry.TryGetValue(entry.Id, out var latest);
                                 revisionCountByEntry.TryGetValue(entry.Id, out var count);

                                 return new
                                        {
                                                entry.Id
                                              , entry.CreatedUtc
                                              , IsDeleted     = entry.DeletedUtc is not null
                                              , entry.DeletedReason
                                              , LatestText    = latest?.Text is { Length: > 0 } text
                                                                        ? text[..Math.Min(text.Length, 120)]
                                                                        : null
                                              , LatestRevisionAt = latest?.CreatedUtc
                                              , RevisionCount    = count
                                        };
                             })
                            .OrderByDescending(entry => entry.CreatedUtc)
                            .ToList();

        return Ok(result);
    }

    /// <summary>Returns all revisions for a given entry, newest first.</summary>
    [HttpGet("entries/{entryId}/revisions")]
    public IActionResult GetRevisions(string entryId)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var revisions = _revisions.GetRevisionsByEntryId(entryId)
                                  .Select(revision => new
                                          {
                                              revision.RevisionId
                                            , revision.EntryId
                                            , revision.CreatedUtc
                                            , revision.Text
                                            , Tags       = revision.Tags.ToArray()
                                            , revision.Mood
                                            , revision.MoodScore
                                            , revision.MoodLevel
                                            , State      = revision.State.ToString()
                                          })
                                  .ToList();

        return Ok(revisions);
    }

    /// <summary>
    /// Appends an admin correction revision to any entry (including deleted ones).
    /// Bypasses the normal domain service to avoid the "entry must be active" guard.
    /// </summary>
    [HttpPost("entries/{entryId}/revisions")]
    public async Task<IActionResult> AddCorrection( string                          entryId
                                                  , [FromBody] AddCorrectionRequest  request)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        if (request.Text.HasNoValue())
            return BadRequest("Text is required.");

        // Accept correction even if entry is soft-deleted
        var entry = _store.Get<JournalEntry>(entryId)
                 ?? _store.GetDeleted<JournalEntry>(entryId);

        if (entry is null)
            return NotFound($"Journal entry '{entryId}' not found.");

        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = entryId
                         , CreatedUtc = DateTimeOffset.UtcNow
                         , Text       = request.Text.Trim()
                         , Tags       = request.Tags ?? []
                         , Mood       = request.Mood
                         , MoodScore  = request.MoodScore
                         , MoodLevel  = request.MoodLevel
                         , State      = JournalEntryState.Active
                       };

        var savedId = await _store.Save(revision, id: revision.RevisionId);

        return Ok(new { revisionId = savedId });
    }

    /// <summary>
    /// Creates a brand new journal entry and its initial revision. Admin use only.
    /// </summary>
    [HttpPost("entries")]
    public async Task<IActionResult> CreateEntry([FromBody] CreateJournalEntryAdminRequest request)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        if (request.Text.HasNoValue())
            return BadRequest("Text is required.");

        var entryId = Guid.NewGuid().ToString("N");
        var now     = DateTimeOffset.UtcNow;

        var entry = new JournalEntry
                    {
                        Id         = entryId
                      , CreatedUtc = now
                    };

        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = entryId
                         , CreatedUtc = now
                         , Text       = request.Text.Trim()
                         , Tags       = request.Tags ?? []
                         , Mood       = request.Mood
                         , MoodScore  = request.MoodScore
                         , MoodLevel  = request.MoodLevel
                         , State      = JournalEntryState.Active
                       };

        await _store.Save(entry, id: entry.Id);
        await _store.Save(revision, id: revision.RevisionId);

        return Ok(new { entryId = entry.Id, revisionId = revision.RevisionId });
    }

    /// <summary>
    /// Soft deletes a journal entry.
    /// </summary>
    [HttpDelete("entries/{entryId}")]
    public async Task<IActionResult> SoftDeleteEntry(string entryId, [FromBody] SoftDeleteJournalAdminRequest? request = null)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entry = _store.Get<JournalEntry>(entryId);
        if (entry is null)
        {
            var alreadyDeleted = _store.GetDeleted<JournalEntry>(entryId);
            if (alreadyDeleted is not null)
                return Ok(new { success = true, message = "Entry is already deleted." });

            return NotFound($"Journal entry '{entryId}' not found.");
        }

        entry.DeletedUtc    = DateTimeOffset.UtcNow;
        entry.DeletedReason = request?.Reason ?? "Deleted via Admin Console";

        await _store.Save(entry, id: entry.Id);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Restores a soft-deleted journal entry.
    /// </summary>
    [HttpPost("entries/{entryId}/restore")]
    public async Task<IActionResult> RestoreEntry(string entryId)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entry = _store.GetDeleted<JournalEntry>(entryId)
                 ?? _store.Get<JournalEntry>(entryId);

        if (entry is null)
            return NotFound($"Journal entry '{entryId}' not found.");

        entry.DeletedUtc    = null;
        entry.DeletedReason = null;

        await _store.Save(entry, id: entry.Id);
        _store.Undelete<JournalEntry>(entryId);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Permanently deletes a journal entry and all of its revisions from SQLite. Admin use only.
    /// </summary>
    [HttpDelete("entries/{entryId}/hard")]
    public IActionResult HardDeleteEntry(string entryId)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entry = _store.Get<JournalEntry>(entryId)
                 ?? _store.GetDeleted<JournalEntry>(entryId);

        if (entry is null)
            return NotFound($"Journal entry '{entryId}' not found.");

        var revisions = _revisions.GetRevisionsByEntryId(entryId);
        foreach (var rev in revisions)
        {
            _store.HardDelete<JournalRevision>(rev.RevisionId);
        }

        _store.HardDelete<JournalEntry>(entryId);

        return Ok(new { success = true, deletedRevisions = revisions.Count });
    }

    /// <summary>
    /// Modifies an existing journal revision directly. Admin use only.
    /// </summary>
    [HttpPut("entries/{entryId}/revisions/{revisionId}")]
    public async Task<IActionResult> UpdateRevision( string                                 entryId
                                                   , string                                 revisionId
                                                   , [FromBody] UpdateJournalRevisionAdminRequest request)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        if (request.Text.HasNoValue())
            return BadRequest("Text is required.");

        var revision = _store.Get<JournalRevision>(revisionId)
                    ?? _store.GetDeleted<JournalRevision>(revisionId);

        if (revision is null || revision.EntryId != entryId)
            return NotFound($"Revision '{revisionId}' for entry '{entryId}' not found.");

        var updated = new JournalRevision
                      {
                          RevisionId = revision.RevisionId
                        , EntryId    = revision.EntryId
                        , CreatedUtc = revision.CreatedUtc
                        , Text       = request.Text.Trim()
                        , Tags       = request.Tags ?? revision.Tags
                        , Mood       = request.Mood ?? revision.Mood
                        , MoodScore  = request.MoodScore ?? revision.MoodScore
                        , MoodLevel  = request.MoodLevel ?? revision.MoodLevel
                        , MediaPaths = revision.MediaPaths
                        , State      = revision.State
                      };

        await _store.Save(updated, id: updated.RevisionId);

        return Ok(new { success = true, revisionId = updated.RevisionId });
    }

    /// <summary>
    /// Sets PartitionKey = NULL for all JournalEntry and JournalRevision rows where
    /// PartitionKey = Id — the fingerprint of records written by old pre-workspace code.
    /// Idempotent: re-running after all rows are repaired returns zeros.
    /// </summary>
    [HttpPost("repair-partition-keys")]
    public IActionResult RepairPartitionKeys()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entryTypeName    = typeof(JournalEntry).FullName    ?? nameof(JournalEntry);
        var revisionTypeName = typeof(JournalRevision).FullName ?? nameof(JournalRevision);

        var repairedAt = DateTimeOffset.UtcNow;

        var repairedEntryIds    = _store.NullifyOrphanedPartitionKeys(entryTypeName);
        var repairedRevisionIds = _store.NullifyOrphanedPartitionKeys(revisionTypeName, jsonIdField: "revisionId");

        var entryDetails = repairedEntryIds.Select(id => new RepairDetail
                                                         {
                                                             RecordType = "Entry"
                                                           , RecordId   = id
                                                           , Field      = "PartitionKey"
                                                           , Before     = id
                                                           , After      = null
                                                           , RepairedAt = repairedAt
                                                         });

        var revisionDetails = repairedRevisionIds.Select(id => new RepairDetail
                                                               {
                                                                   RecordType = "Revision"
                                                                 , RecordId   = id
                                                                 , Field      = "PartitionKey"
                                                                 , Before     = id
                                                                 , After      = null
                                                                 , RepairedAt = repairedAt
                                                               });

        var allDetails = entryDetails.Concat(revisionDetails).ToArray();

        return Ok(new
                  {
                      EntriesRepaired     = repairedEntryIds.Count
                    , RevisionsRepaired   = repairedRevisionIds.Count
                    , RepairedEntryIds    = repairedEntryIds.ToArray()
                    , RepairedRevisionIds = repairedRevisionIds.ToArray()
                    , RepairDetails       = allDetails
                  });
    }
}

public sealed record CreateJournalEntryAdminRequest
{
    public string    Text      { get; init; } = string.Empty;
    public string[]? Tags      { get; init; }
    public string?   Mood      { get; init; }
    public int?      MoodScore { get; init; }
    public int?      MoodLevel { get; init; }
}

public sealed record UpdateJournalRevisionAdminRequest
{
    public string    Text      { get; init; } = string.Empty;
    public string[]? Tags      { get; init; }
    public string?   Mood      { get; init; }
    public int?      MoodScore { get; init; }
    public int?      MoodLevel { get; init; }
}

public sealed record SoftDeleteJournalAdminRequest
{
    public string? Reason { get; init; }
}

public sealed record AddCorrectionRequest
{
    public string    Text      { get; init; } = string.Empty;
    public string[]? Tags      { get; init; }
    public string?   Mood      { get; init; }
    public int?      MoodScore { get; init; }
    public int?      MoodLevel { get; init; }
}

public sealed record RepairDetail
{
    public string         RecordType { get; init; } = string.Empty;
    public string         RecordId   { get; init; } = string.Empty;
    public string         Field      { get; init; } = string.Empty;
    public string?        Before     { get; init; }
    public string?        After      { get; init; }
    public DateTimeOffset RepairedAt { get; init; }
}
