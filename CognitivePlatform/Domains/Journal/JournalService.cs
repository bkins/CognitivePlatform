using System;
using System.Collections.Generic;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Models;

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

    public JournalService (IObjectStore               store
                         , IJournalRevisionRepository revisionRepository
                         , ILogger<JournalService>    logger)
    {
        _store              = store;
        _revisionRepository = revisionRepository;
        _logger             = logger;
    }

    public async Task<string> AddEntryAsync(string                 text
                                          , IReadOnlyList<string> tags
                                          , string?               mood
                                          , int?                  moodScore
                                          , int?                  moodLevel
                                          , IReadOnlyList<string> mediaPaths)
    {
        var entryId = Guid.NewGuid().ToString("N");

        var entry = new JournalEntry
                    {
                            Id         = entryId,
                            CreatedUtc = DateTimeOffset.UtcNow
                    };

        var revision = new JournalRevision
                       {
                               RevisionId = Guid.NewGuid().ToString("N")
                             , EntryId    = entryId
                             , CreatedUtc = DateTimeOffset.UtcNow
                             , Text       = text
                             , Tags       = tags
                             , Mood       = mood
                             , MoodScore  = moodScore
                             , MoodLevel  = moodLevel
                             , MediaPaths = mediaPaths
                       };

        var actualEntryId = _store.Save(entry, entry.Id);
        if (entryId != actualEntryId) _logger.LogWarning("The 'EntryId' that was intended to be used was not what was created by the journal service.  Look into why.");
        
        _store.Save(revision, revision.RevisionId);

        return actualEntryId;
    }


    public JournalRevision EditEntry(string                 entryId
                                   , string?                text       = null
                                   , IReadOnlyList<string>? tags       = null
                                   , string?                mood       = null
                                   , int?                   moodScore  = null
                                   , int?                   moodLevel  = null
                                   , IReadOnlyList<string>? mediaPaths = null)
    {
        var entry = _store.Get<JournalEntry>(entryId);
        if (entry is null) throw new KeyNotFoundException($"Entry with Id '{entryId}' not found.");
        if (entry.DeletedUtc is not null) throw new InvalidOperationException($"Journal entry with Id '{entryId}' has already been deleted.");

        var latest = GetLatestRevision(entryId);
        if (latest is null) throw  new InvalidOperationException($"Entry with Id '{entryId}' does not have a revision.");

        var newRevision = new JournalRevision
                          {
                                  RevisionId = Guid.NewGuid().ToString("N")
                                , EntryId    = entryId
                                , CreatedUtc = DateTimeOffset.UtcNow
                                , Text       = text       ?? latest.Text
                                , Tags       = tags       ?? latest.Tags
                                , Mood       = mood       ?? latest.Mood
                                , MoodScore  = moodScore  ?? latest.MoodScore
                                , MoodLevel  = moodLevel  ?? latest.MoodLevel
                                , MediaPaths = mediaPaths ?? latest.MediaPaths
                          };
        _store.Save(newRevision
                  , newRevision.RevisionId);

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
        //NOTE: this may call the repository multiple times — that’s fine for now.
        // Correctness > optimization, for now. When batching is implemented, it will be done once, in the repository.
        
        
        var entries = _store.List<JournalEntry>(fromUtc: fromUtc
                                              , toUtc: toUtc);

        return entries.Select(entry =>
                      {
                          var revisions = _revisionRepository.GetRevisionsByEntryId(entry.Id);

                          if (revisions.Count == 0) return null;

                          return new JournalEntryWithRevision(entry
                                                            , revisions[0]
                                                            , revisions.Count > 1);
                      })
                      .Where(revision => revision is not null)
                      .OrderBy(revision => revision!.Entry.CreatedUtc)
                      .ToList()!;
    }
    
    public JournalEntry? GetEntry (string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty."
                                      , nameof(id));

        return _store.Get<JournalEntry>(id
                                      , partitionKey: null);
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
        var entry = _store.Get<JournalEntry>(id, partitionKey: null)!;
        if (entry is null) return false;

        entry.DeletedUtc    = DateTime.UtcNow;
        entry.DeletedReason = reason;

        _store.Save(entry, entry.Id);

        return true;
    }
    
    [Obsolete("Use DeleteEntry(string id, string reason)")]
    public bool DeleteEntry (string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty."
                                      , nameof(id));

        return _store.SoftDelete<JournalEntry>(id
                                             , partitionKey: null);
    }

    public List<JournalEntry> ListEntriesOnThisDay (int month
                                                  , int day)
    {
        return _store.List<JournalEntry>(partitionKey: nameof(JournalEntry))
                     .Where(e => e.CreatedUtc.Month == month && e.CreatedUtc.Day == day)
                     .OrderByDescending(e => e.CreatedUtc)
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

            // If the caller already supplied some kind of path, keep it as-is.
            if (trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                result.Add(trimmed);
            }
            else
            {
                // Treat as a simple file name and tuck it under a logical per-entry folder.
                result.Add($"Media/{entryId}/{trimmed}");
            }
        }

        return result;
    }

}