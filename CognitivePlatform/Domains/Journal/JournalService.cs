using System;
using System.Collections.Generic;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Data;

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
    private readonly IObjectStore _store;

    public JournalService (IObjectStore store)
    {
        _store = store;
    }

    public string AddEntry (string               text
                          , IEnumerable<string>? tags       = null
                          , string?              context    = null
                          , string?              mood       = null
                          , int?                 moodScore  = null
                          , IEnumerable<string>? mediaPaths = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Journal text cannot be empty."
                                      , nameof(text));

        var entryId = Guid.NewGuid().ToString("N");

        var cleanedTags = tags?.Where(tag => tag.HasValue())
                               .Select(tag => tag.Trim())
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();

        var entry = new JournalEntry
                    {
                            Id         = entryId
                          , Text       = text.Trim()
                          , CreatedUtc = DateTimeOffset.UtcNow
                          , Tags       = cleanedTags
                          , Context = string.IsNullOrWhiteSpace(context)
                                              ? null
                                              : context.Trim()
                          , Mood = string.IsNullOrWhiteSpace(mood)
                                           ? null
                                           : mood.Trim()
                          , MoodScore = moodScore
                          , MoodLevel = moodScore.HasValue
                                                ? MapMoodLevel(moodScore.Value)
                                                : null
                          , MediaPaths = NormalizeMediaPaths(mediaPaths
                                                           , entryId)
                    };
        _store.Save(entry
                  , partitionKey: null
                  , id: entryId);

        return entryId;
    }

    public IReadOnlyList<JournalEntry> ListEntries (DateTimeOffset? fromUtc = null
                                                  , DateTimeOffset? toUtc   = null)
    {
        var entries = _store.List<JournalEntry>(partitionKey: null
                                              , fromUtc: fromUtc
                                              , toUtc: toUtc);

        return entries.OrderBy(entry => entry.CreatedUtc)
                      .ToList();
    }

    public JournalEntry? GetEntry (string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty."
                                      , nameof(id));

        return _store.Get<JournalEntry>(id
                                      , partitionKey: null);
    }

    public JournalEntry? GetById(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("id cannot be empty.", nameof(id));

        // Your entries are stored using Guid.ToString("N")
        var stringId = id.ToString("N");

        return GetEntry(stringId);
    }
    
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

    private static MoodLevel MapMoodLevel (int score)
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