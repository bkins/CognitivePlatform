using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Tests;

public class ConversationMetadataStoreTests
{
    private readonly Mock<IObjectStore>      _storeMock = new();
    private readonly ConversationMetadataStore _sut;

    public ConversationMetadataStoreTests()
    {
        _sut = new ConversationMetadataStore(_storeMock.Object);
    }

    // ── UpsertAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_SavesWithConversationMetaPartitionAndPrefixedKey()
    {
        var metadata = new ConversationMetadata
                       {
                               ConversationId = "conv-abc"
                             , CreatedUtc     = DateTime.UtcNow
                             , LastActiveUtc  = DateTime.UtcNow
                             , MessageCount   = 2
                       };

        _storeMock
            .Setup(store => store.Save(It.IsAny<ConversationMetadata>()
                                      , It.IsAny<string>()
                                      , It.IsAny<string>()))
            .ReturnsAsync("saved-id");

        await _sut.UpsertAsync(metadata);

        _storeMock.Verify(store => store.Save(
                              It.IsAny<ConversationMetadata>()
                            , "conversation_meta"
                            , "convmeta:conv-abc")
                        , Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingRecord_WhenCalledTwiceWithSameConversationId()
    {
        var conversationId = "conv-update";
        var savedCalls     = new List<int>();

        _storeMock
            .Setup(store => store.Save(It.IsAny<ConversationMetadata>()
                                      , "conversation_meta"
                                      , $"convmeta:{conversationId}"))
            .Callback<ConversationMetadata, string?, string?>((meta, _, _) => savedCalls.Add(meta.MessageCount))
            .ReturnsAsync($"convmeta:{conversationId}");

        var first = new ConversationMetadata { ConversationId = conversationId, MessageCount = 2 };
        await _sut.UpsertAsync(first);

        var second = new ConversationMetadata { ConversationId = conversationId, MessageCount = 4 };
        await _sut.UpsertAsync(second);

        Assert.Equal(2, savedCalls.Count);
        Assert.Equal(2, savedCalls[0]);
        Assert.Equal(4, savedCalls[1]);
    }

    // ── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsMetadata_WhenFound()
    {
        var expected = new ConversationMetadata { ConversationId = "conv-found", MessageCount = 6 };

        _storeMock
            .Setup(store => store.Get<ConversationMetadata>("convmeta:conv-found", "conversation_meta"))
            .Returns(expected);

        var result = await _sut.GetAsync("conv-found");

        Assert.NotNull(result);
        Assert.Equal("conv-found", result!.ConversationId);
        Assert.Equal(6,            result.MessageCount);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        _storeMock
            .Setup(store => store.Get<ConversationMetadata>("conv-missing", "conversation_meta"))
            .Returns((ConversationMetadata?)null);

        var result = await _sut.GetAsync("conv-missing");

        Assert.Null(result);
    }

    // ── ListAllAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAllAsync_ExcludesSoftDeletedRecords()
    {
        var active  = new ConversationMetadata { ConversationId = "active",  IsDeleted = false, LastActiveUtc = DateTime.UtcNow };
        var deleted = new ConversationMetadata { ConversationId = "deleted", IsDeleted = true,  LastActiveUtc = DateTime.UtcNow };

        _storeMock
            .Setup(store => store.List<ConversationMetadata>("conversation_meta", null, null))
            .Returns(new[] { active, deleted });

        var result = (await _sut.ListAllAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("active", result[0].ConversationId);
    }

    [Fact]
    public async Task ListAllAsync_OrdersByLastActiveUtcDescending()
    {
        var older  = new ConversationMetadata { ConversationId = "older",  LastActiveUtc = DateTime.UtcNow.AddHours(-2) };
        var newest = new ConversationMetadata { ConversationId = "newest", LastActiveUtc = DateTime.UtcNow };
        var middle = new ConversationMetadata { ConversationId = "middle", LastActiveUtc = DateTime.UtcNow.AddHours(-1) };

        _storeMock
            .Setup(store => store.List<ConversationMetadata>("conversation_meta", null, null))
            .Returns(new[] { older, newest, middle });

        var result = (await _sut.ListAllAsync()).ToList();

        Assert.Equal(3,        result.Count);
        Assert.Equal("newest", result[0].ConversationId);
        Assert.Equal("middle", result[1].ConversationId);
        Assert.Equal("older",  result[2].ConversationId);
    }

    [Fact]
    public async Task ListAllAsync_ReturnsEmpty_WhenNoRecordsExist()
    {
        _storeMock
            .Setup(store => store.List<ConversationMetadata>("conversation_meta", null, null))
            .Returns(Array.Empty<ConversationMetadata>());

        var result = await _sut.ListAllAsync();

        Assert.Empty(result);
    }

    // ── SoftDeleteAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteAsync_CallsStoreSoftDeleteWithCorrectPartitionAndId()
    {
        var conversationId = "conv-del";

        _storeMock
            .Setup(store => store.SoftDelete<ConversationMetadata>("convmeta:conv-del", "conversation_meta"))
            .Returns(true);

        await _sut.SoftDeleteAsync(conversationId);

        _storeMock.Verify(store => store.SoftDelete<ConversationMetadata>("convmeta:conv-del", "conversation_meta")
                        , Times.Once);
    }
}
