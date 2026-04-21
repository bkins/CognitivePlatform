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
}

public sealed record AddCorrectionRequest
{
    public string    Text      { get; init; } = string.Empty;
    public string[]? Tags      { get; init; }
    public string?   Mood      { get; init; }
    public int?      MoodScore { get; init; }
    public int?      MoodLevel { get; init; }
}
