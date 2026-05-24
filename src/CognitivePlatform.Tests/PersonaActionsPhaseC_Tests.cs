using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for the Phase C NL actions added to <see cref="PersonaActions"/>:
/// ConfirmMemory and ListPendingMemories.
/// </summary>
public class PersonaActionsPhaseC_Tests
{
    private readonly Mock<IPersonaService>          _serviceMock   = new();
    private readonly Mock<IPersonaSessionManager>   _sessionMock   = new();
    private readonly Mock<IPersonaStore>            _storeMock     = new();
    private readonly Mock<IMemoryConfirmationQueue> _queueMock     = new();
    private readonly PersonaActions                 _actions;

    private const string SessionId = "test-session-phase-c";

    public PersonaActionsPhaseC_Tests()
    {
        _storeMock.Setup(store => store.ConfirmMemoryAsync(It.IsAny<Guid>()
                                                         , It.IsAny<Guid>()
                                                         , It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _actions = new PersonaActions(_serviceMock.Object
                                    , _sessionMock.Object
                                    , _storeMock.Object
                                    , _queueMock.Object);

        PersonaActions.SetSessionId(SessionId);
    }

    // ================================================================
    // ConfirmMemory
    // ================================================================

    [Fact]
    public async Task ConfirmMemory_ReturnsNoPendingMessage_WhenQueueIsEmpty()
    {
        _queueMock.Setup(queue => queue.HasPending(SessionId)).Returns(false);

        var result = await _actions.ConfirmMemory();

        Assert.Equal("No pending memories to confirm.", result);
        _storeMock.Verify(store => store.ConfirmMemoryAsync(It.IsAny<Guid>()
                                                          , It.IsAny<Guid>()
                                                          , It.IsAny<CancellationToken>())
                        , Times.Never);
    }

    [Fact]
    public async Task ConfirmMemory_ConfirmsMemory_WhenOneIsPending()
    {
        var personaId = Guid.NewGuid();
        var memoryId  = Guid.NewGuid();
        var memory    = new PersonaMemory
                        {
                                Id        = memoryId
                              , PersonaId = personaId
                              , Content   = "She was a quiet reader."
                              , Type      = MemoryType.Behavioral
                        };

        _queueMock.Setup(queue => queue.HasPending(SessionId)).Returns(true);
        _queueMock.Setup(queue => queue.Dequeue(SessionId)).Returns(memory);

        var result = await _actions.ConfirmMemory();

        _storeMock.Verify(store => store.ConfirmMemoryAsync(memoryId, personaId, It.IsAny<CancellationToken>())
                        , Times.Once);

        Assert.Contains("She was a quiet reader.", result);
        Assert.Contains("Behavioral",              result);
    }

    // ================================================================
    // ListPendingMemories
    // ================================================================

    [Fact]
    public void ListPendingMemories_ReturnsNoMemoriesMessage_WhenQueueIsEmpty()
    {
        _queueMock.Setup(queue => queue.Peek(SessionId, It.IsAny<int>()))
                  .Returns(new List<PersonaMemory>());

        var result = _actions.ListPendingMemories();

        Assert.Equal("No memories are currently pending confirmation.", result);
    }

    [Fact]
    public void ListPendingMemories_ReturnsSummary_WhenMemoriesPending()
    {
        var memories = new List<PersonaMemory>
        {
            new() { Id = Guid.NewGuid(), Content = "She had a gentle laugh.", Type = MemoryType.Emotional, Confidence = 0.7f }
          , new() { Id = Guid.NewGuid(), Content = "She smelled like lavender.", Type = MemoryType.Sensory, Confidence = 0.85f }
        };

        _queueMock.Setup(queue => queue.Peek(SessionId, It.IsAny<int>()))
                  .Returns(memories);

        var result = _actions.ListPendingMemories();

        Assert.Contains("Pending memories",          result);
        Assert.Contains("She had a gentle laugh.",   result);
        Assert.Contains("She smelled like lavender.", result);
        Assert.Contains("Emotional",                 result);
        Assert.Contains("Sensory",                   result);
    }
}
