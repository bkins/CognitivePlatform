using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Domains.Journal.Interfaces;

public interface IJournalService
{
    Task<string> AddEntryAsync (string                text
                              , IReadOnlyList<string> tags
                              , string?               mood
                              , int?                  moodScore
                              , int?                  moodLevel
                              , IReadOnlyList<string> mediaPaths);

    JournalRevision EditEntry (string                 entryId
                             , string?                text           = null
                             , IReadOnlyList<string>? tags           = null
                             , bool                   clearTags      = false
                             , string?                mood           = null
                             , bool                   clearMood      = false
                             , int?                   moodScore      = null
                             , bool                   clearMoodScore = false
                             , int?                   moodLevel      = null
                             , IReadOnlyList<string>? mediaPaths     = null);

    IReadOnlyList<JournalEntryWithRevision> ListEntries (DateTimeOffset? fromUtc = null
                                                       , DateTimeOffset? toUtc   = null);

    public JournalEntry? GetEntry (string id);

    public bool DeleteEntry (string id, string reason);

    public List<JournalEntry> ListEntriesOnThisDay (int month
                                                  , int day);

    JournalEntryWithRevision GetById (string id);

    bool Exists (Guid journalId);
}