using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Tests;

public class ConversationTurnStoreTests
{
    private readonly Mock<IObjectStore>  _storeMock = new();
    private readonly ConversationTurnStore _sut;

    public ConversationTurnStoreTests()
    {
        _sut = new ConversationTurnStore(_storeMock.Object);
    }

    // ── SaveAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_PersistsEntityWithConversationIdAsPartitionKey()
    {
        var conversationId = "conv-123";
        var turn           = new ConversationTurn(
                                 UserMessage:      "hello"
                               , AssistantMessage: "hi there"
                               , OccurredAt:       DateTimeOffset.UtcNow
                               , Path:             TurnPath.FastPath
                               , ActionName:       null
                               , Succeeded:        true);

        _storeMock
            .Setup(store => store.Save(It.IsAny<PersistedConversationTurn>()
                                     , It.IsAny<string>()
                                     , It.IsAny<string>()))
            .ReturnsAsync("saved-id");

        await _sut.SaveAsync(conversationId, turn);

        _storeMock.Verify(store => store.Save(
                              It.Is<PersistedConversationTurn>(entity =>
                                  entity.ConversationId   == conversationId
                               && entity.UserMessage      == "hello"
                               && entity.AssistantMessage == "hi there"
                               && entity.Path             == TurnPath.FastPath
                               && entity.Succeeded        == true)
                            , conversationId
                            , null)
                        , Times.Once);
    }

    [Fact]
    public async Task SaveAsync_NormalisesOccurredAtToUtc()
    {
        var localOffset = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.FromHours(5));
        var turn        = new ConversationTurn(
                              UserMessage:      "x"
                            , AssistantMessage: "y"
                            , OccurredAt:       localOffset
                            , Path:             TurnPath.Interpreter
                            , ActionName:       null
                            , Succeeded:        null);

        PersistedConversationTurn? captured = null;

        _storeMock
            .Setup(store => store.Save(It.IsAny<PersistedConversationTurn>()
                                     , It.IsAny<string>()
                                     , It.IsAny<string>()))
            .Callback<PersistedConversationTurn, string?, string?>((entity, _, _) => captured = entity)
            .ReturnsAsync("id");

        await _sut.SaveAsync("conv", turn);

        Assert.NotNull(captured);
        Assert.Equal(TimeSpan.Zero, captured!.OccurredAt.Offset);
        Assert.Equal(localOffset.ToUniversalTime(), captured.OccurredAt);
    }

    // ── GetRecent ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetRecent_ReturnsEmpty_WhenNoTurnsStored()
    {
        _storeMock
            .Setup(store => store.List<PersistedConversationTurn>("conv-empty", null, null))
            .Returns(Array.Empty<PersistedConversationTurn>());

        var result = _sut.GetRecent("conv-empty", 20);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRecent_ReturnsTurnsOrderedOldestFirst()
    {
        var oldest  = PersistedTurn("first",  DateTimeOffset.UtcNow.AddMinutes(-5));
        var middle  = PersistedTurn("second", DateTimeOffset.UtcNow.AddMinutes(-3));
        var newest  = PersistedTurn("third",  DateTimeOffset.UtcNow.AddMinutes(-1));

        _storeMock
            .Setup(store => store.List<PersistedConversationTurn>("conv-order", null, null))
            .Returns(new[] { oldest, middle, newest });

        var result = _sut.GetRecent("conv-order", 20);

        Assert.Equal(3, result.Count);
        Assert.Equal("first",  result[0].UserMessage);
        Assert.Equal("second", result[1].UserMessage);
        Assert.Equal("third",  result[2].UserMessage);
    }

    [Fact]
    public void GetRecent_RespectsLastCap_ReturningOnlyMostRecentN()
    {
        // Store returns oldest-first (insertion order); TakeLast(n) picks the tail
        var entities = Enumerable.Range(1, 10)
                                 .Select(index => PersistedTurn($"msg-{index}"
                                                               , DateTimeOffset.UtcNow.AddMinutes(index - 11)))
                                 .ToArray();

        _storeMock
            .Setup(store => store.List<PersistedConversationTurn>("conv-cap", null, null))
            .Returns(entities);

        var result = _sut.GetRecent("conv-cap", 3);

        Assert.Equal(3,       result.Count);
        Assert.Equal("msg-8", result[0].UserMessage);
        Assert.Equal("msg-9", result[1].UserMessage);
        Assert.Equal("msg-10", result[2].UserMessage);
    }

    [Fact]
    public void GetRecent_ReturnsFewer_WhenStoredCountIsBelowCap()
    {
        var entities = Enumerable.Range(1, 2)
                                 .Select(index => PersistedTurn($"msg-{index}"
                                                               , DateTimeOffset.UtcNow.AddMinutes(-index)))
                                 .ToArray();

        _storeMock
            .Setup(store => store.List<PersistedConversationTurn>("conv-few", null, null))
            .Returns(entities);

        var result = _sut.GetRecent("conv-few", 20);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetRecent_QueriesStoreByConversationIdPartitionKey()
    {
        var conversationId = "scoped-conv";

        _storeMock
            .Setup(store => store.List<PersistedConversationTurn>(conversationId, null, null))
            .Returns(Array.Empty<PersistedConversationTurn>())
            .Verifiable();

        _sut.GetRecent(conversationId, 10);

        _storeMock.Verify(store => store.List<PersistedConversationTurn>(conversationId, null, null)
                        , Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PersistedConversationTurn PersistedTurn(string userMessage, DateTimeOffset occurredAt) =>
        new()
        {
                ConversationId   = "test"
              , UserMessage      = userMessage
              , AssistantMessage = "response"
              , OccurredAt       = occurredAt
              , Path             = TurnPath.Interpreter
        };
}
