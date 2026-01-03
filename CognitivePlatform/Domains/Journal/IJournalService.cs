namespace CognitivePlatform.Api.Domains.Journal;

public interface IJournalService
{
    string AddEntry (string               text
                   , IEnumerable<string>? tags       = null
                   , string?              context    = null
                   , string?              mood       = null
                   , int?                 moodScore  = null
                   , IEnumerable<string>? mediaPaths = null);

    IReadOnlyList<JournalEntry> ListEntries (DateTimeOffset? fromUtc = null
                                           , DateTimeOffset? toUtc   = null);

    public JournalEntry? GetEntry (string id);

    public bool DeleteEntry (string id);

    public List<JournalEntry> ListEntriesOnThisDay (int month
                                                  , int day);
}