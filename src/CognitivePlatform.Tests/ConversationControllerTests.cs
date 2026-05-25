using Moq;
using Microsoft.AspNetCore.Mvc;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Tests;

public class ConversationControllerTests
{
    private readonly Mock<IConversationOrchestrator>  _orchestratorMock  = new();
    private readonly Mock<ITelemetrySink>             _telemetryMock     = new();
    private readonly Mock<IConversationTurnStore>     _turnStoreMock     = new();
    private readonly Mock<IConversationMetadataStore> _metadataStoreMock = new();
    private readonly ConversationController            _sut;

    public ConversationControllerTests()
    {
        var telemetryContext = new TelemetryContext { SessionId = "test-session" };

        _sut = new ConversationController(
                   _orchestratorMock.Object
                 , _telemetryMock.Object
                 , telemetryContext
                 , _turnStoreMock.Object
                 , _metadataStoreMock.Object);
    }

    // ── ListConversations ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListConversations_Returns200_WithConversationList()
    {
        var conversations = new[]
                            {
                                new ConversationMetadata { ConversationId = "conv-1", MessageCount = 4 }
                              , new ConversationMetadata { ConversationId = "conv-2", MessageCount = 2 }
                            };

        _metadataStoreMock
            .Setup(store => store.ListAllAsync())
            .ReturnsAsync(conversations);

        var result = await _sut.ListConversations();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(conversations, ok.Value);
    }

    [Fact]
    public async Task ListConversations_Returns200_WithEmptyList_WhenNoConversationsExist()
    {
        _metadataStoreMock
            .Setup(store => store.ListAllAsync())
            .ReturnsAsync(Array.Empty<ConversationMetadata>());

        var result = await _sut.ListConversations();

        var ok  = Assert.IsType<OkObjectResult>(result);
        var val = Assert.IsAssignableFrom<IEnumerable<ConversationMetadata>>(ok.Value);
        Assert.Empty(val);
    }

    // ── GetMetadata ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetadata_Returns200_WhenConversationExists()
    {
        var metadata = new ConversationMetadata { ConversationId = "conv-123", MessageCount = 10 };

        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-123"))
            .ReturnsAsync(metadata);

        var result = await _sut.GetMetadata("conv-123");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(metadata, ok.Value);
    }

    [Fact]
    public async Task GetMetadata_Returns404_WhenConversationNotFound()
    {
        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-missing"))
            .ReturnsAsync((ConversationMetadata?)null);

        var result = await _sut.GetMetadata("conv-missing");

        Assert.IsType<NotFoundResult>(result);
    }

    // ── RenameConversation ────────────────────────────────────────────────────

    [Fact]
    public async Task RenameConversation_Returns200_AndUpdatesName_WhenConversationExists()
    {
        var metadata = new ConversationMetadata { ConversationId = "conv-rename", Name = null };

        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-rename"))
            .ReturnsAsync(metadata);

        _metadataStoreMock
            .Setup(store => store.UpsertAsync(It.IsAny<ConversationMetadata>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.RenameConversation("conv-rename", new RenameConversationRequest("My Chat"));

        var ok      = Assert.IsType<OkObjectResult>(result);
        var updated = Assert.IsType<ConversationMetadata>(ok.Value);
        Assert.Equal("My Chat", updated.Name);
    }

    [Fact]
    public async Task RenameConversation_Returns404_WhenConversationNotFound()
    {
        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-missing"))
            .ReturnsAsync((ConversationMetadata?)null);

        var result = await _sut.RenameConversation("conv-missing", new RenameConversationRequest("Whatever"));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RenameConversation_PersistsUpdatedMetadata()
    {
        var metadata = new ConversationMetadata { ConversationId = "conv-persist" };

        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-persist"))
            .ReturnsAsync(metadata);

        ConversationMetadata? persisted = null;
        _metadataStoreMock
            .Setup(store => store.UpsertAsync(It.IsAny<ConversationMetadata>()))
            .Callback<ConversationMetadata>(saved => persisted = saved)
            .Returns(Task.CompletedTask);

        await _sut.RenameConversation("conv-persist", new RenameConversationRequest("New Name"));

        Assert.NotNull(persisted);
        Assert.Equal("New Name", persisted!.Name);
    }

    // ── DeleteConversation ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteConversation_Returns204_WhenConversationExists()
    {
        var metadata = new ConversationMetadata { ConversationId = "conv-del" };

        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-del"))
            .ReturnsAsync(metadata);

        _metadataStoreMock
            .Setup(store => store.SoftDeleteAsync("conv-del"))
            .Returns(Task.CompletedTask);

        var result = await _sut.DeleteConversation("conv-del");

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteConversation_Returns404_WhenConversationNotFound()
    {
        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-ghost"))
            .ReturnsAsync((ConversationMetadata?)null);

        var result = await _sut.DeleteConversation("conv-ghost");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteConversation_CallsSoftDeleteOnStore()
    {
        var metadata = new ConversationMetadata { ConversationId = "conv-soft-del" };

        _metadataStoreMock
            .Setup(store => store.GetAsync("conv-soft-del"))
            .ReturnsAsync(metadata);

        _metadataStoreMock
            .Setup(store => store.SoftDeleteAsync("conv-soft-del"))
            .Returns(Task.CompletedTask);

        await _sut.DeleteConversation("conv-soft-del");

        _metadataStoreMock.Verify(store => store.SoftDeleteAsync("conv-soft-del"), Times.Once);
    }
}
