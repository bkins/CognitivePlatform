using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class MemoryConfirmationQueueTests
{
    private readonly MemoryConfirmationQueue _queue = new();

    // ================================================================
    // HasPending
    // ================================================================

    [Fact]
    public void HasPending_ReturnsFalse_WhenQueueIsEmpty()
    {
        var result = _queue.HasPending("session-1");

        Assert.False(result);
    }

    [Fact]
    public void HasPending_ReturnsTrue_AfterEnqueue()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), Content = "She loved jazz." };

        _queue.Enqueue("session-1", memory);

        Assert.True(_queue.HasPending("session-1"));
    }

    // ================================================================
    // Enqueue / Dequeue
    // ================================================================

    [Fact]
    public void Enqueue_Then_Dequeue_ReturnsSameMemory()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), Content = "She wore blue dresses." };

        _queue.Enqueue("session-2", memory);
        var dequeued = _queue.Dequeue("session-2");

        Assert.NotNull(dequeued);
        Assert.Equal(memory.Id,      dequeued.Id);
        Assert.Equal(memory.Content, dequeued.Content);
    }

    [Fact]
    public void Dequeue_ReturnsNull_WhenQueueIsEmpty()
    {
        var result = _queue.Dequeue("session-unknown");

        Assert.Null(result);
    }

    [Fact]
    public void Dequeue_ReducesPendingCount_WhenItemExists()
    {
        var first  = new PersonaMemory { Id = Guid.NewGuid(), Content = "She was shy." };
        var second = new PersonaMemory { Id = Guid.NewGuid(), Content = "She played piano." };

        _queue.Enqueue("session-3", first);
        _queue.Enqueue("session-3", second);

        _queue.Dequeue("session-3");

        Assert.True(_queue.HasPending("session-3"));

        _queue.Dequeue("session-3");

        Assert.False(_queue.HasPending("session-3"));
    }

    // ================================================================
    // Peek
    // ================================================================

    [Fact]
    public void Peek_ReturnsMemories_WithoutRemovingThem()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), Content = "She had a loud laugh." };

        _queue.Enqueue("session-4", memory);

        var peeked = _queue.Peek("session-4", count: 3);

        Assert.Single(peeked);
        Assert.True(_queue.HasPending("session-4"));
    }

    [Fact]
    public void Peek_ReturnsEmpty_WhenQueueIsEmpty()
    {
        var peeked = _queue.Peek("session-empty", count: 3);

        Assert.Empty(peeked);
    }

    [Fact]
    public void Peek_RespectsCountLimit()
    {
        for (var index = 0; index < 5; index++)
            _queue.Enqueue("session-5", new PersonaMemory { Id = Guid.NewGuid(), Content = $"Memory {index}" });

        var peeked = _queue.Peek("session-5", count: 2);

        Assert.Equal(2, peeked.Count);
        Assert.True(_queue.HasPending("session-5"));
    }

    // ================================================================
    // Session isolation
    // ================================================================

    [Fact]
    public void Enqueue_DoesNotAffect_OtherSessions()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), Content = "She liked tea." };

        _queue.Enqueue("session-A", memory);

        Assert.False(_queue.HasPending("session-B"));
        Assert.Null(_queue.Dequeue("session-B"));
    }
}
