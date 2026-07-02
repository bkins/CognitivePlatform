using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

/// <summary>
/// Tracks provisional memories awaiting user confirmation per conversation session.
/// In-memory, keyed by conversationId. Thread-safe.
/// </summary>
public interface IMemoryConfirmationQueue
{
    /// <summary>Appends a provisional memory to the tail of the queue for this session.</summary>
    void Enqueue(string conversationId, PersonaMemory memory);

    /// <summary>
    /// Removes and returns the next pending memory for this session,
    /// or <c>null</c> if the queue is empty.
    /// </summary>
    PersonaMemory? Dequeue(string conversationId);

    /// <summary>
    /// Returns up to <paramref name="count"/> pending memories without removing them.
    /// </summary>
    IReadOnlyList<PersonaMemory> Peek(string conversationId, int count = 3);

    /// <summary>Returns the number of pending memories for this session.</summary>
    int Count(string conversationId);

    /// <summary>Returns <c>true</c> when at least one memory is pending for this session.</summary>
    bool HasPending(string conversationId);
}
