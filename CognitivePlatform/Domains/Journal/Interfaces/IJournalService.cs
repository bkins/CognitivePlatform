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

    /// <summary>
    /// Returns entries whose latest revision text, tags, or mood contain the keyword
    /// (case-insensitive). Both fromUtc and toUtc are optional date bounds.
    /// </summary>
    IReadOnlyList<JournalEntryWithRevision> SearchEntries (string          keyword
                                                         , DateTimeOffset? fromUtc = null
                                                         , DateTimeOffset? toUtc   = null);

    public JournalEntry? GetEntry (string id);

    public bool DeleteEntry (string id, string reason);

    public List<JournalEntry> ListEntriesOnThisDay (int month
                                                  , int day);

    JournalEntryWithRevision GetById (string id);

    /// <summary>
    /// Returns all non-deleted journal entries in reverse-chronological order
    /// (most recent = position 1), each paired with its 1-based display position.
    /// Positions are stable across filtered listing calls so users can always
    /// reference an entry by the number shown in the most recent listing.
    /// </summary>
    IReadOnlyList<(int Position, JournalEntryWithRevision EntryWithRevision)> GetOrderedEntries();

    /// <summary>
    /// Resolves a 1-based display position to its JournalEntryWithRevision,
    /// or null if the position is out of range. Uses the same ordering as
    /// <see cref="GetOrderedEntries"/>.
    /// </summary>
    JournalEntryWithRevision? ResolveByPosition(int position);

    bool Exists (Guid journalId);
}