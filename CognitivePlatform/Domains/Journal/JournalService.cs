using System;
using System.Collections.Generic;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalService
{
    private readonly IObjectStore _store;

    public JournalService(IObjectStore store)
    {
        _store = store;
    }

    public string AddEntry(string text, IEnumerable<string>? tags = null, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Journal text cannot be empty.", nameof(text));

        var entry = new JournalEntry
                    {
                            Text       = text.Trim(),
                            Tags       = tags is null ? null : new List<string>(tags),
                            Context    = context,
                            CreatedUtc = DateTimeOffset.UtcNow
                    };

        return _store.Save(entry);
    }

    public IReadOnlyList<JournalEntry> ListEntries(DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        return _store.List<JournalEntry>(null, fromUtc, toUtc);
    }

    public JournalEntry? GetEntry(string id)
    {
        return _store.Get<JournalEntry>(id);
    }

    public void DeleteEntry(string id)
    {
        _store.SoftDelete<JournalEntry>(id);
    }
    
    public List<JournalEntry> ListEntriesOnThisDay(int month, int day)
    {
        return _store.List<JournalEntry>(partitionKey: nameof(JournalEntry))
                     .Where(e => e.CreatedUtc.Month == month && e.CreatedUtc.Day == day)
                     .OrderByDescending(e => e.CreatedUtc)
                     .ToList();
    }

}