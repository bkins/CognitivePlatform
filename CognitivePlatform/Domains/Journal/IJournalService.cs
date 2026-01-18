using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Domains.Journal;

public interface IJournalService
{
    Task<string> AddEntryAsync (string                text
                              , IReadOnlyList<string> tags
                              , string?               mood
                              , int?                  moodScore
                              , int?                  moodLevel
                              , IReadOnlyList<string> mediaPaths);

    JournalRevision EditEntry (string                 entryId
                             , string?                text       = null
                             , IReadOnlyList<string>? tags       = null
                             , string?                mood       = null
                             , int?                   moodScore  = null
                             , int?                   moodLevel  = null
                             , IReadOnlyList<string>? mediaPaths = null);

    IReadOnlyList<JournalEntryWithRevision> ListEntries (DateTimeOffset? fromUtc = null
                                                       , DateTimeOffset? toUtc   = null);

    public JournalEntry? GetEntry (string id);

    public bool DeleteEntry (string id);

    public List<JournalEntry> ListEntriesOnThisDay (int month
                                                  , int day);

    JournalEntryWithRevision GetById (string id);

    [Obsolete("Use GetById (string id) instead")]
    JournalEntry? GetById (Guid id);
}