using System.Collections.Concurrent;
using CognitivePlatform.Api.Domains.Journal.Interfaces;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class InMemoryJournalDraftRepository : IJournalDraftRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<JournalDraft>> _bySession = new();

    public Task AddAsync(JournalDraft draft, CancellationToken ct = default)
    {
        // If you later want session scoping, you can add SessionId to JournalDraft.
        // For now, keep it simple and global-ish.
        var queue = _bySession.GetOrAdd("default", _ => new ConcurrentQueue<JournalDraft>());
        queue.Enqueue(draft);

        return Task.CompletedTask;
    }
}
