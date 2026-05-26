using Moq;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;

/// <summary>
/// Verifies the CheckForInsightFollowThroughAsync behaviour wired into
/// ConversationOrchestrator (EPIC-06 Phase C.2).
/// Drives the orchestrator end-to-end with a mocked IInsightHistoryStore so
/// RecordOutcomeAsync calls can be asserted precisely.
/// </summary>
[Collection("LlmSharedState")]
public class OrchestratorInsightFollowThroughTests
{
    private readonly Mock<ICapabilityRegistry>  _registryMock     = new();
    private readonly Mock<IInterpreter>        _interpreterMock  = new();
    private readonly Mock<IExecutionEngine>    _executionMock    = new();
    private readonly Mock<IFastPathResolver>   _fastPathMock     = new();
    private readonly Mock<IWorkspaceContext>    _workspaceContextMock = new();
    private readonly Mock<ILlmRouter>          _routerMock       = new();
    private readonly Mock<IIdempotencyStore>   _idempotencyMock  = new();
    private readonly Mock<IInsightEngine>      _engineMock       = new();
    private readonly Mock<IInsightHistoryStore> _historyMock      = new();
    private readonly Mock<IActivityLog>        _activityLogMock  = new();
    private readonly Mock<ITelemetrySink>      _telemetryMock    = new();
    private readonly Mock<ILlmRateLimiter>         _rateLimiterMock  = new();
    private readonly Mock<IConversationTurnStore>  _turnStoreMock    = new();

    private readonly ConversationContextStore _contextStore     = new();
    private readonly TelemetryContext         _telemetryContext = new() { SessionId = "test-session" };
    private readonly LlmModelCatalog          _modelCatalog     = new();
    private readonly LlmProviderDefaults      _providerDefaults = new();

    public OrchestratorInsightFollowThroughTests()
    {
        _idempotencyMock
            .Setup(store => store.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConverseResponse?)null);

        _idempotencyMock
            .Setup(store => store.StoreAsync(It.IsAny<Guid>()
                                           , It.IsAny<ConverseResponse>()
                                           , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _fastPathMock
            .Setup(resolver => resolver.TryExtractWorkspacePrefix(It.IsAny<string>()
                                                                 , out It.Ref<string?>.IsAny
                                                                 , out It.Ref<string?>.IsAny))
            .Returns(false);

        _workspaceContextMock
            .Setup(context => context.SetActiveWorkspaceAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns(false);

        _activityLogMock
            .Setup(log => log.LogAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _rateLimiterMock
            .Setup(limiter => limiter.GetLatest(It.IsAny<string>()))
            .Returns(LlmRateLimitSnapshot.Empty);

        _rateLimiterMock
            .Setup(limiter => limiter.IsExhausted(It.IsAny<string>()))
            .Returns(false);

        _historyMock
            .Setup(store => store.RecordEmittedAsync(It.IsAny<IReadOnlyList<Insight>>()
                                                   , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _historyMock
            .Setup(store => store.RecordOutcomeAsync(It.IsAny<string>()
                                                   , It.IsAny<InsightOutcome>()
                                                   , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Engine returns no insights by default.
        _engineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Insight>());
    }

    private ConversationOrchestrator BuildOrchestrator() =>
        new(_registryMock.Object
          , _interpreterMock.Object
          , _executionMock.Object
          , _contextStore
          , _telemetryMock.Object
          , _fastPathMock.Object
          , _workspaceContextMock.Object
          , _routerMock.Object
          , _idempotencyMock.Object
          , _telemetryContext
          , _engineMock.Object
          , _historyMock.Object
          , _activityLogMock.Object
          , _modelCatalog
          , _providerDefaults
          , _rateLimiterMock.Object
          , _turnStoreMock.Object
          , new Mock<IConversationMetadataStore>().Object
          , new TaskComplexityClassifier());

    private static ConverseRequest Request(string input, string sessionId = "test-session") =>
        new() { SessionId = sessionId, Input = input };

    private void StubInterpreterReturns(string actionName) =>
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                   , It.IsAny<ConversationContext>()
                                                                                                                                   , It.IsAny<TaskComplexity>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName  = actionName
                                , DebugInfo   = "stub interpretation"
                                , FailureType = InterpreterFailureType.None
                          });

    private void StubRegistryReturns(ActionMetadata action)
    {
        _registryMock.Setup(registry => registry.GetAll()).Returns(new[] { action });
    }

    private void StubExecutionReturns(string output) =>
        _executionMock
            .Setup(exec => exec.ExecuteAsync(It.IsAny<ActionMetadata>()
                                           , It.IsAny<IDictionary<string, string>>()
                                           , It.IsAny<string>()
                                           , It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);

    private static ActionMetadata SimpleAction(string name) =>
        new() { Name = name, Category = "General" };

    // ====================================================================
    // CheckForInsightFollowThroughAsync — interpreter path
    // ====================================================================

    [Fact]
    public async Task FollowThrough_RecordsActedOn_AndClearsInsights_WhenActionMatchesSuggestedAction()
    {
        var action = SimpleAction("ListTasksSummary");
        StubInterpreterReturns("ListTasksSummary");
        StubRegistryReturns(action);
        StubExecutionReturns("Here are your tasks.");

        var orchestrator = BuildOrchestrator();
        var context      = _contextStore.GetOrCreate("test-session");
        context.SetLastEmittedInsights(new[]
        {
            new EmittedInsightRef("tasks.open-tasks-reminder", "ListTasksSummary")
        });

        await orchestrator.ConverseAsync(Request("list my tasks"));

        _historyMock.Verify(store => store.RecordOutcomeAsync(
                                "tasks.open-tasks-reminder"
                              , InsightOutcome.ActedOn
                              , It.IsAny<CancellationToken>())
                          , Times.Once);

        Assert.Empty(context.LastEmittedInsights);
    }

    [Fact]
    public async Task FollowThrough_DoesNothing_WhenNoInsightMatchesResolvedAction()
    {
        var action = SimpleAction("AddTask");
        StubInterpreterReturns("AddTask");
        StubRegistryReturns(action);
        StubExecutionReturns("Task added.");

        var orchestrator = BuildOrchestrator();
        var context      = _contextStore.GetOrCreate("test-session");
        context.SetLastEmittedInsights(new[]
        {
            new EmittedInsightRef("tasks.open-tasks-reminder", "ListTasksSummary")
        });

        await orchestrator.ConverseAsync(Request("add a task"));

        // No match found — RecordOutcomeAsync must not be called.
        _historyMock.Verify(store => store.RecordOutcomeAsync(
                                It.IsAny<string>()
                              , It.IsAny<InsightOutcome>()
                              , It.IsAny<CancellationToken>())
                          , Times.Never);
    }
}
