using System.Collections.Concurrent;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

/// <summary>
/// In-memory, session-keyed queue of provisional memories awaiting user confirmation.
/// Registered as a singleton so it survives across scoped DI lifetimes.
/// Thread-safe: each session's queue is wrapped in a dedicated lock object.
/// </summary>
public class MemoryConfirmationQueue : IMemoryConfirmationQueue
{
    // Each value is a (lock, Queue) pair so enqueue/dequeue on the same session
    // are serialized without blocking unrelated sessions.
    private readonly ConcurrentDictionary<string, SessionQueue> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public void Enqueue(string conversationId, PersonaMemory memory)
    {
        var sessionQueue = _sessions.GetOrAdd(conversationId, _ => new SessionQueue());

        lock (sessionQueue.SyncRoot)
        {
            sessionQueue.Queue.Enqueue(memory);
        }
    }

    public PersonaMemory? Dequeue(string conversationId)
    {
        if (!_sessions.TryGetValue(conversationId, out var sessionQueue))
            return null;

        lock (sessionQueue.SyncRoot)
        {
            return sessionQueue.Queue.Count > 0
                       ? sessionQueue.Queue.Dequeue()
                       : null;
        }
    }

    public IReadOnlyList<PersonaMemory> Peek(string conversationId, int count = 3)
    {
        if (!_sessions.TryGetValue(conversationId, out var sessionQueue))
            return [];

        lock (sessionQueue.SyncRoot)
        {
            return sessionQueue.Queue.Take(count).ToList();
        }
    }

    public bool HasPending(string conversationId)
    {
        if (!_sessions.TryGetValue(conversationId, out var sessionQueue))
            return false;

        lock (sessionQueue.SyncRoot)
        {
            return sessionQueue.Queue.Count > 0;
        }
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    private sealed class SessionQueue
    {
        public object        SyncRoot { get; } = new();
        public Queue<PersonaMemory> Queue { get; } = new();
    }
}
