using System;
using System.Collections.Generic;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Domains.Journal;

/// DOMAIN SERVICE
/// ----------------
/// Owns domain meaning and business rules.
/// - Defines what this domain object IS and how it behaves.
/// - Talks directly to persistence (ObjectStore).
/// - Does NOT know about Knowledge, inboxes, UI, or cross-domain concepts.
///
/// Rule of thumb:
/// If the Knowledge system disappeared tomorrow,
/// this service should still exist unchanged.

/// <summary>
/// ObjectStore is infrastructure.
/// Domain Services own meaning.
/// KnowledgeService coordinates meaning across domains.
/// </summary>
public sealed class JournalService : IJournalService
{
    private readonly IObjectStore               _store;
    private readonly ILogger<JournalService>    _logger;
    private readonly IJournalRevisionRepository _revisionRepository;
    private readonly IJournalDraftRepository    _draftRepository;
    private readonly IWorkspaceContext          _workspaceContext;

    public JournalService (IObjectStore               store
                         , IJournalRevisionRepository revisionRepository
                         , IJournalDraftRepository    draftRepository
                         , ILogger<JournalService>    logger
                         , IWorkspaceContext          workspaceContext)
    {
        _store              = store;
        _revisionRepository = revisionRepository;
        _draftRepository    = draftRepository;
        _logger             = logger;
        _workspaceContext   = workspaceContext;
    }

    public async Task<string> AddEntryAsync(string                 text
                                          , IReadOnlyList<string> tags
                                          , string?               mood
                                          , int?                  moodScore
                                          , int?                  moodLevel
                                          , IReadOnlyList<string> mediaPaths)
    {
        var entryId = Guid.NewGuid();

        var entry = new JournalEntry
                    {
                            Id = entryId.ToString("N")
                          , CreatedUtc = DateTimeOffset.UtcNow
                    };

        var revision = new JournalRevision
                       {
                               RevisionId = Guid.NewGuid().ToString("N")
                             , EntryId    = entryId.ToString("N")
                             , CreatedUtc = DateTimeOffset.UtcNow
                             , Text       = text
                             , Tags       = tags
                             , Mood       = mood
                             , MoodScore  = moodScore
                             , MoodLevel  = moodLevel
                             , MediaPaths = mediaPaths
                       };

        var actualEntryId = await _store.Save(entry
                                            , partitionKey: _workspaceContext.ActivePartitionKey
                                            , id:           entry.Id);
        if (entryId.ToString("N") != actualEntryId)
            _logger.LogWarning("The 'EntryId' that was intended to be used was not what was created by the journal service. Look into why.");

        await _store.Save(revision
                        , partitionKey: _workspaceContext.ActivePartitionKey
                        , id:           revision.RevisionId);

        // TODO: Not sure if this is needed. Consider removing the concept of a "draft" and just having the latest
        //  revision be the "draft" until it's finalized? The draft concept was originally intended to support a
        //  "save draft" vs "publish" flow, but that may be overcomplicating things for now.
        await _draftRepository.AddAsync(new JournalDraft
                                        {
                                                Id         = entryId
                                              , Text       = text
                                              , Tags       = tags
                                              , Mood       = mood
                                              , MoodScore  = moodScore
                                              , State      = JournalDraftState.Local
                                              , CreatedUtc = DateTimeOffset.UtcNow
                                        });
        return actualEntryId;
    }

    public JournalRevision EditEntry(string                 entryId
                                   , string?                text           = null
                                   , IReadOnlyList<string>? tags           = null
                                   , bool                   clearTags      = false
                                   , string?                mood           = null
                                   , bool                   clearMood      = false
                                   , int?                   moodScore      = null
                                   , bool                   clearMoodScore = false
                                   , int?                   moodLevel      = null
                                   , IReadOnlyList<string>? mediaPaths     = null)
    {
        var entry = _store.Get<JournalEntry>(entryId);
        if (entry is null) throw new KeyNotFoundException($"Entry with Id '{entryId}' not found.");
        if (entry.DeletedUtc is not null) throw new InvalidOperationException($"Journal entry with Id '{entryId}' has already been deleted.");

        var latest = GetLatestRevision(entryId);

        var newRevision = new JournalRevision
                          {
                                  RevisionId = Guid.NewGuid().ToString("N")
                                , EntryId    = entryId
                                , CreatedUtc = DateTimeOffset.UtcNow
                                , Text       = text                          ?? latest.Text
                                , Tags       = clearTags      ? []           : tags      ?? latest.Tags
                                , Mood       = clearMood      ? null         : mood      ?? latest.Mood
                                , MoodScore  = clearMoodScore ? null         : moodScore ?? latest.MoodScore
                                , MoodLevel  = moodLevel                     ?? latest.MoodLevel
                                , MediaPaths = mediaPaths                    ?? latest.MediaPaths
                          };
        _store.Save(newRevision
                  , partitionKey: _workspaceContext.ActivePartitionKey
                  , id:           newRevision.RevisionId);

        return newRevision;
    }

    private JournalRevision GetLatestRevision(string entryId)
    {
        var revisions = _revisionRepository.GetRevisionsByEntryId(entryId);

        return revisions.FirstOrDefault()
                        ?? throw new InvalidOperationException($"JournalEntry '{entryId}' has no revisions.");
    }

    public IReadOnlyList<JournalEntryWithRevision> ListEntries(DateTimeOffset?  fromUtc = null
                                                              , DateTimeOffset? toUtc   = null)
    {
        var entries = _store.List<JournalEntry>(partitionKey: _workspaceContext.ActivePartitionKey
                                              , fromUtc: fromUtc
                                              , toUtc:   toUtc);

        return entries.Select(entry =>
                      {
                          var revisions = _revisionRepository.GetRevisionsByEntryId(entry.Id);

                          if (revisions.Count == 0) return null;

                          return new JournalEntryWithRevision(entry
                                                            , revisions[0]
                                                            , revisions.Count > 1);
                      })
                      .Where(revision => revision is not null
                                      && revision.Entry.DeletedUtc == null)
                      .OrderBy(revision => revision!.Entry.CreatedUtc)
                      .ToList()!;
    }

    public IReadOnlyList<JournalEntryWithRevision> SearchEntries (string          keyword
                                                                , DateTimeOffset? fromUtc = null
                                                                , DateTimeOffset? toUtc   = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return ListEntries(fromUtc, toUtc);

        var needle = keyword.Trim();

        return ListEntries(fromUtc, toUtc)
               .Where(ewr => ContainsKeyword(ewr.LatestRevision, needle))
               .ToList();
    }

    private static bool ContainsKeyword(JournalRevision revision, string keyword)
    {
        if (revision.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        if (revision.Mood is not null
         && revision.Mood.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        return revision.Tags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public JournalEntry? GetEntry (string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty.", nameof(id));

        // Null partition key intentionally used for ID-based lookups — matches all partitions.
        // This allows retrieval by absolute ID regardless of the current workspace.
        return _store.Get<JournalEntry>(id, partitionKey: null);
    }

    public IReadOnlyList<(int Position, JournalEntryWithRevision EntryWithRevision)> GetOrderedEntries()
    {
        return ListEntries()
               .OrderByDescending(entryWithRevision => entryWithRevision.Entry.CreatedUtc)
               .Select((entryWithRevision, index) => (Position: index + 1, EntryWithRevision: entryWithRevision))
               .ToList();
    }

    public JournalEntryWithRevision? ResolveByPosition(int position)
    {
        var ordered = GetOrderedEntries();

        if (position < 1 || position > ordered.Count)
            return null;

        return ordered[position - 1].EntryWithRevision;
    }

    public JournalEntryWithRevision GetById(string id)
    {
        var entry = _store.Get<JournalEntry>(id);
        if (entry is null) throw new KeyNotFoundException($"JournalEntry {id} not found.");

        var revisions = _revisionRepository.GetRevisionsByEntryId(id);

        if (revisions.Count == 0) throw new InvalidOperationException($"JournalEntry {id} has no revisions.");

        var latest    = revisions[0];
        var wasEdited = revisions.Count > 1;

        return new JournalEntryWithRevision(entry, latest, wasEdited);
    }

    public bool Exists (Guid journalId)
    {
        var entry = _store.Get<JournalEntry>(journalId.ToString("N"));
        return entry is not null;
    }

    public bool DeleteEntry(string id, string reason)
    {
        // Null partition key for Get — allows deletion by absolute ID regardless of workspace.
        var entry = _store.Get<JournalEntry>(id, partitionKey: null);

        if (entry is null) return false;
        if (entry.DeletedUtc is not null) throw new InvalidOperationException("Entry is already deleted.");

        entry.DeletedUtc    = DateTimeOffset.UtcNow;
        entry.DeletedReason = reason;

        _store.Save(entry
                  , partitionKey: _workspaceContext.ActivePartitionKey
                  , id:           entry.Id);

        return true;
    }

    public List<JournalEntry> ListEntriesOnThisDay (int month
                                                  , int day)
    {
        return _store.List<JournalEntry>(partitionKey: _workspaceContext.ActivePartitionKey)
                     .Where(entry => entry.CreatedUtc.Month == month
                                  && entry.CreatedUtc.Day   == day
                                  && entry.DeletedUtc       == null)
                     .OrderByDescending(entry => entry.CreatedUtc)
                     .ToList();
    }

    public IReadOnlyList<JournalRevision> GetRevisionHistory(string entryId)
    {
        return _revisionRepository.GetRevisionsByEntryId(entryId)
                                  .OrderBy(revision => revision.CreatedUtc)
                                  .ToList();
    }

    public static MoodLevel MapMoodLevel (int score)
    {
        return score switch
        {
                <= 1 => MoodLevel.VeryNegative
              , 2    => MoodLevel.Negative
              , 3    => MoodLevel.Neutral
              , 4    => MoodLevel.Positive
              , >= 5 => MoodLevel.VeryPositive
        };
    }

    public static string MapMoodEmoji (MoodLevel mood)
    {
        return mood switch
        {
                MoodLevel.VeryNegative => "😢"
              , MoodLevel.Negative     => "🙁"
              , MoodLevel.Neutral      => "😐"
              , MoodLevel.Positive     => "🙂"
              , MoodLevel.VeryPositive => "😄"
              , _                      => "❓"
        };
    }

    private static List<string> NormalizeMediaPaths (IEnumerable<string>? mediaPaths
                                                   , string               entryId)
    {
        var result = new List<string>();

        if (mediaPaths is null) return result;

        foreach (var raw in mediaPaths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmed = raw.Trim();

            if (trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                result.Add(trimmed);
            }
            else
            {
                result.Add($"Media/{entryId}/{trimmed}");
            }
        }

        return result;
    }

}
