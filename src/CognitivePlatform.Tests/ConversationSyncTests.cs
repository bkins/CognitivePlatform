using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class ConversationSyncTests
{
    private readonly Mock<IConversationOrchestrator>  _orchestratorMock = new();
    private readonly Mock<ITelemetrySink>             _telemetryMock    = new();
    private readonly Mock<IConversationTurnStore>     _turnStoreMock    = new();
    private readonly Mock<IConversationMetadataStore> _metadataStoreMock= new();
    private readonly TelemetryContext                 _telemetryContext = new() { SessionId = "test-session" };

    private readonly ConversationController _controller;

    public ConversationSyncTests()
    {
        _controller = new ConversationController(
            _orchestratorMock.Object
          , _telemetryMock.Object
          , _telemetryContext
          , _turnStoreMock.Object
          , _metadataStoreMock.Object);
    }

    [Fact]
    public async Task SyncConversations_ReturnsAllActiveConversations_WhenSinceIsNull()
    {
        var now = DateTime.UtcNow;
        var metas = new List<ConversationMetadata>
        {
            new() { ConversationId = "c1", Name = "Conv 1", LastActiveUtc = now, MessageCount = 2, IsDeleted = false }
          , new() { ConversationId = "c2", Name = "Conv 2", LastActiveUtc = now.AddMinutes(-10), MessageCount = 1, IsDeleted = false }
        };

        var turns = new List<ConversationTurn>
        {
            new("hello", "hi there", DateTimeOffset.UtcNow, TurnPath.FastPath)
        };

        _metadataStoreMock.Setup(store => store.ListAllAsync()).ReturnsAsync(metas);
        _turnStoreMock.Setup(store => store.GetRecent("c1", 50)).Returns(turns);
        _turnStoreMock.Setup(store => store.GetRecent("c2", 50)).Returns(new List<ConversationTurn>());

        var result = await _controller.SyncConversations(null, null);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var syncResponse = Assert.IsType<SyncResponseDto>(okResult.Value);

        Assert.Equal("Default", syncResponse.Workspace);
        Assert.Equal(2, syncResponse.UpdatedConversations.Count);
        Assert.Equal("c1", syncResponse.UpdatedConversations[0].Id);
        Assert.Equal(2, syncResponse.UpdatedConversations[0].Messages.Count);
    }

    [Fact]
    public async Task SyncConversations_FiltersTurnsBySinceTimestamp()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var metas = new List<ConversationMetadata>
        {
            new() { ConversationId = "c1", Name = "Conv 1", LastActiveUtc = DateTime.UtcNow, MessageCount = 2, IsDeleted = false }
        };

        var turns = new List<ConversationTurn>
        {
            new("old question", "old answer", cutoff.AddMinutes(-10), TurnPath.Interpreter)
          , new("new question", "new answer", cutoff.AddMinutes(1), TurnPath.FastPath)
        };

        _metadataStoreMock.Setup(store => store.ListAllAsync()).ReturnsAsync(metas);
        _turnStoreMock.Setup(store => store.GetRecent("c1", 50)).Returns(turns);

        var result = await _controller.SyncConversations("CustomWorkspace", cutoff);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var syncResponse = Assert.IsType<SyncResponseDto>(okResult.Value);

        Assert.Equal("CustomWorkspace", syncResponse.Workspace);
        Assert.Single(syncResponse.UpdatedConversations);
        Assert.Equal(2, syncResponse.UpdatedConversations[0].Messages.Count);
        Assert.Equal("new question", syncResponse.UpdatedConversations[0].Messages[0].Content);
        Assert.Equal("new answer", syncResponse.UpdatedConversations[0].Messages[1].Content);
    }

    [Fact]
    public async Task SyncConversations_IgnoresSoftDeletedConversations()
    {
        var metas = new List<ConversationMetadata>
        {
            new() { ConversationId = "c1", Name = "Active", LastActiveUtc = DateTime.UtcNow, MessageCount = 1, IsDeleted = false }
          , new() { ConversationId = "c2", Name = "Deleted", LastActiveUtc = DateTime.UtcNow, MessageCount = 1, IsDeleted = true }
        };

        _metadataStoreMock.Setup(store => store.ListAllAsync()).ReturnsAsync(metas);
        _turnStoreMock.Setup(store => store.GetRecent("c1", 50)).Returns(new List<ConversationTurn>());

        var result = await _controller.SyncConversations(null, null);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var syncResponse = Assert.IsType<SyncResponseDto>(okResult.Value);

        Assert.Single(syncResponse.UpdatedConversations);
        Assert.Equal("c1", syncResponse.UpdatedConversations[0].Id);
    }

    [Fact]
    public async Task SyncConversations_PopulatesNonEmptyReasoningAndSenderDetails()
    {
        var metas = new List<ConversationMetadata>
        {
            new() { ConversationId = "c1", Name = "Conv 1", LastActiveUtc = DateTime.UtcNow, MessageCount = 2, IsDeleted = false }
        };

        var turns = new List<ConversationTurn>
        {
            new("add task milk", "Added task 'milk'", DateTimeOffset.UtcNow, TurnPath.FastPath, "Tasks.CreateTask")
        };

        _metadataStoreMock.Setup(store => store.ListAllAsync()).ReturnsAsync(metas);
        _turnStoreMock.Setup(store => store.GetRecent("c1", 50)).Returns(turns);

        var result = await _controller.SyncConversations(null, null);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var syncResponse = Assert.IsType<SyncResponseDto>(okResult.Value);

        var assistantMsg = syncResponse.UpdatedConversations[0].Messages[1];
        Assert.Equal("assistant", assistantMsg.Sender);
        Assert.Equal("Tasks.CreateTask", assistantMsg.ActionName);
        Assert.True(assistantMsg.WasFastPath);
        Assert.NotEmpty(assistantMsg.ReasoningContent);
    }
}
