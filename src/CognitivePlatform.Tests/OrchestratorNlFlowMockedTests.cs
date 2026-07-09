using Moq;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Interpreter.FastPath;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

/// <summary>
/// T-3A — Mocked LLM E2E Flow tests.
/// Verifies that well-known FastPath natural language phrases resolve to the correct action
/// in ConversationOrchestrator without calling the LLM.
/// </summary>
public sealed class OrchestratorNlFlowMockedTests
{
    private readonly ActionRegistry                  _registry             = new();
    private readonly Mock<IInterpreter>             _interpreterMock      = new();
    private readonly Mock<IExecutionEngine>         _executionMock        = new();
    private readonly Mock<IWorkspaceContext>        _workspaceContextMock = new();
    private readonly Mock<ILlmRouter>               _routerMock           = new();
    private readonly Mock<IIdempotencyStore>        _idempotencyMock      = new();
    private readonly Mock<IInsightEngine>           _engineMock           = new();
    private readonly Mock<IActivityLog>             _activityLogMock      = new();
    private readonly Mock<ITelemetrySink>           _telemetryMock        = new();
    private readonly Mock<ILlmRateLimiter>          _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore>   _turnStoreMock        = new();
    private readonly Mock<IConversationMetadataStore> _metadataStoreMock    = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new LlmProviderDefaults(Options.Create(new LlmClientSettings()));
    private readonly FastPathResolver            _fastPath;

    public OrchestratorNlFlowMockedTests()
    {
        _fastPath = new FastPathResolver(_registry, new DailyRecordCommandParser());

        _idempotencyMock
            .Setup(store => store.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConverseResponse?)null);

        _activityLogMock
            .Setup(log => log.LogAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _engineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Insight>());

        _rateLimiterMock
            .Setup(limiter => limiter.GetCurrentSnapshot(It.IsAny<string>()))
            .Returns(LlmRateLimitSnapshot.Empty);

        _workspaceContextMock
            .Setup(w => w.ActiveWorkspace)
            .Returns("personal");
    }

    private ConversationOrchestrator BuildOrchestrator()
    {
        var capabilityRegistry = new CapabilityRegistry(_registry);
        return new ConversationOrchestrator(
            capabilityRegistry
          , _interpreterMock.Object
          , _executionMock.Object
          , _contextStore
          , _telemetryMock.Object
          , _fastPath
          , _workspaceContextMock.Object
          , _routerMock.Object
          , _idempotencyMock.Object
          , _telemetryContext
          , _engineMock.Object
          , _historyStore
          , _activityLogMock.Object
          , _modelCatalog
          , _providerDefaults
          , _rateLimiterMock.Object
          , _turnStoreMock.Object
          , _metadataStoreMock.Object
          , new TaskComplexityClassifier());
    }

    [Fact]
    public async Task ConverseAsync_FastPath_AddTask_FiresCorrectly()
    {
        var orchestrator = BuildOrchestrator();

        _executionMock
            .Setup(e => e.ExecuteAsync(It.Is<ActionMetadata>(a => a.Name == "AddTask")
                                     , It.Is<IDictionary<string, string>>(p => p["shortDescription"] == "Buy milk")
                                     , It.IsAny<string>()
                                     , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Task created: Buy milk");

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "task: Buy milk"
        });

        Assert.True(response.WasFastPath);
        Assert.Equal("AddTask", response.SelectedAction);
        Assert.Contains("Task created: Buy milk", response.Message);
        _interpreterMock.VerifyNoOtherCalls(); // Verify LLM is not called
    }

    [Fact]
    public async Task ConverseAsync_FastPath_ListTasks_FiresCorrectly()
    {
        var orchestrator = BuildOrchestrator();

        _executionMock
            .Setup(e => e.ExecuteAsync(It.Is<ActionMetadata>(a => a.Name == "ListTasks")
                                     , It.Is<IDictionary<string, string>>(p => p["includeCompleted"] == "true")
                                     , It.IsAny<string>()
                                     , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Task List...");

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "list my tasks"
        });

        Assert.True(response.WasFastPath);
        Assert.Equal("ListTasks", response.SelectedAction);
        Assert.Equal("Task List...", response.Message);
        _interpreterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConverseAsync_FastPath_OpenDay_FiresCorrectly()
    {
        var orchestrator = BuildOrchestrator();

        _executionMock
            .Setup(e => e.ExecuteAsync(It.Is<ActionMetadata>(a => a.Name == "OpenDay")
                                     , It.Is<IDictionary<string, string>>(p => p["openingText"] == "Focus on refactoring")
                                     , It.IsAny<string>()
                                     , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Day opened.");

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "Plan: Focus on refactoring"
        });

        Assert.True(response.WasFastPath);
        Assert.Equal("OpenDay", response.SelectedAction);
        Assert.Equal("Day opened.", response.Message);
        _interpreterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConverseAsync_FastPath_AddJournalEntry_FiresCorrectly()
    {
        var orchestrator = BuildOrchestrator();

        _executionMock
            .Setup(e => e.ExecuteAsync(It.Is<ActionMetadata>(a => a.Name == "AddJournalEntry")
                                     , It.Is<IDictionary<string, string>>(p => p["text"] == "Had a good meeting")
                                     , It.IsAny<string>()
                                     , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Journal entry saved.");

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "journal: Had a good meeting"
        });

        Assert.True(response.WasFastPath);
        Assert.Equal("AddJournalEntry", response.SelectedAction);
        Assert.Equal("Journal entry saved.", response.Message);
        _interpreterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConverseAsync_FastPath_DeleteTask_AsDestructiveAction_RequiresConfirmation()
    {
        var orchestrator = BuildOrchestrator();

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "delete task 12"
        });

        Assert.True(response.WasFastPath);
        Assert.True(response.IsConfirmationRequired);
        Assert.Equal("DeleteTask", response.SelectedAction);
        Assert.NotNull(response.ConfirmationPrompt);
        _executionMock.VerifyNoOtherCalls(); // Execution does not happen yet
        _interpreterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConverseAsync_WorkspacePrefix_SwitchesWorkspaceAndFiresCorrectly()
    {
        var orchestrator = BuildOrchestrator();

        _executionMock
            .Setup(e => e.ExecuteAsync(It.Is<ActionMetadata>(a => a.Name == "AddTask")
                                     , It.Is<IDictionary<string, string>>(p => p["shortDescription"] == "Buy milk")
                                     , It.IsAny<string>()
                                     , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Task created: Buy milk");

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "work: task: Buy milk"
        });

        _workspaceContextMock.Verify(w => w.SetActiveWorkspaceAsync("work"), Times.Once);
        Assert.True(response.WasFastPath);
        Assert.Equal("AddTask", response.SelectedAction);
        Assert.Equal("Task created: Buy milk", response.Message);
        _interpreterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConverseAsync_FastPath_ListActions_FiresCorrectly()
    {
        var orchestrator = BuildOrchestrator();

        _executionMock
            .Setup(e => e.ExecuteAsync(It.Is<ActionMetadata>(a => a.Name == "ListActions")
                                     , It.IsAny<IDictionary<string, string>>()
                                     , It.IsAny<string>()
                                     , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Action list...");

        var response = await orchestrator.ConverseAsync(new ConverseRequest
        {
            SessionId = "test-session"
          , Input     = "what can you do"
        });

        Assert.True(response.WasFastPath);
        Assert.Equal("ListActions", response.SelectedAction);
        Assert.Equal("Action list...", response.Message);
        _interpreterMock.VerifyNoOtherCalls();
    }
}
