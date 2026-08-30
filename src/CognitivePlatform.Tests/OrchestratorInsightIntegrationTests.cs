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
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

// ENH-08 + BACK-11. Drives the orchestrator end-to-end with stubbed dependencies
// to verify:
//   - the engine runs after execution and ConverseResponse.Insights is populated;
//   - context.LastEmittedInsights is updated;
//   - Message is woven when insights present, raw when zero;
//   - weave failure → un-woven Message + structured Insights[] still populated +
//     insight.weave_failed activity event logged;
//   - context.RecordTurn is called with the un-woven message before the engine fires;
//   - context.ReplaceLatestTurn is called with the woven message after weave;
//   - turns are recorded across the FastPath and Interpreter branches with the
//     correct TurnPath stamp.
//
// Zero real LLM clients are constructed; the router and any insight producers are
// Moq objects. The history store is the real InMemoryInsightHistoryStore.
[Collection("LlmSharedState")]
public class OrchestratorInsightIntegrationTests
{
    private readonly Mock<ICapabilityRegistry>  _registryMock     = new();
    private readonly Mock<IInterpreter>        _interpreterMock  = new();
    private readonly Mock<IExecutionEngine>    _executionMock    = new();
    private readonly Mock<IFastPathResolver>   _fastPathMock     = new();
    private readonly Mock<IWorkspaceContext>    _workspaceContextMock = new();
    private readonly Mock<ILlmRouter>          _routerMock       = new();
    private readonly Mock<IIdempotencyStore>   _idempotencyMock  = new();
    private readonly Mock<IInsightEngine>      _engineMock       = new();
    private readonly Mock<IInsightProvider>    _providerMock     = new(); // unused but engine constructor compatibility
    private readonly Mock<IActivityLog>        _activityLogMock  = new();
    private readonly Mock<ITelemetrySink>      _telemetryMock    = new();
    private readonly Mock<ILlmRateLimiter>         _rateLimiterMock  = new();
    private readonly Mock<IConversationTurnStore>  _turnStoreMock    = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new LlmProviderDefaults(Options.Create(new LlmClientSettings()));

    public OrchestratorInsightIntegrationTests()
    {
        _idempotencyMock
            .Setup(store => store.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConverseResponse?)null);

        _idempotencyMock
            .Setup(store => store.StoreAsync(It.IsAny<Guid>()
                                           , It.IsAny<ConverseResponse>()
                                           , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // FastPath off by default
        _fastPathMock
            .Setup(resolver => resolver.TryExtractWorkspacePrefix(It.IsAny<string>()
                                                                 , out It.Ref<string?>.IsAny
                                                                 , out It.Ref<string?>.IsAny))
            .Returns(false);

        // WorkspaceContext: default no-op.
        _workspaceContextMock
            .Setup(context => context.SetActiveWorkspaceAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns(false);

        // Activity log: accept anything.
        _activityLogMock
            .Setup(log => log.LogAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Rate limiter: optimistic defaults (no data recorded yet).
        _rateLimiterMock
            .Setup(limiter => limiter.GetLatest(It.IsAny<string>()))
            .Returns(LlmRateLimitSnapshot.Empty);
        _rateLimiterMock
            .Setup(limiter => limiter.IsExhausted(It.IsAny<string>()))
            .Returns(false);
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
          , _historyStore
          , _activityLogMock.Object
          , _modelCatalog
          , _providerDefaults
          , _rateLimiterMock.Object
          , _turnStoreMock.Object
          , new Mock<IConversationMetadataStore>().Object
          , new TaskComplexityClassifier());

    private static ConverseRequest Request(string input, string sessionId = "test-session") =>
        new() { SessionId = sessionId, Input = input };

    private void StubEngineEmits(params Insight[] insights) =>
        _engineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(insights);

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
        _registryMock.Setup(reg => reg.GetAll()).Returns(new[] { action });
    }

    private void StubExecutionReturns(string output) =>
        _executionMock
            .Setup(exec => exec.ExecuteAsync(It.IsAny<ActionMetadata>()
                                           , It.IsAny<IDictionary<string, string>>()
                                           , It.IsAny<string>()
                                           , It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);

    private static ActionMetadata SimpleAction(string name = "DoSomething", bool isReplayable = false) =>
        new() { Name = name, Category = "General", IsReplayable = isReplayable };

    private static Insight Insight(string deduplicationKey,
                                   string message = "follow-on suggestion") =>
        new()
        {
                Message          = message
              , DeduplicationKey = deduplicationKey
              , Category         = InsightCategory.Reflection
        };

    // ====================================================================
    // PHASE A: ENGINE → WEAVE → RESPONSE WIRING (closes BACK-11)
    // ====================================================================

    [Fact]
    public async Task EngineEmitsInsights_WeaveSucceeds_MessageIsWoven_AndInsightsOnResponse()
    {
        StubInterpreterReturns("DoSomething");
        StubRegistryReturns(SimpleAction());
        StubExecutionReturns("raw exec output");

        var emitted = Insight("reflection.signal-1", "Want to journal that?");
        StubEngineEmits(emitted);

        _routerMock
            .Setup(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                             , It.IsAny<string>()
                                             , It.IsAny<IReadOnlyList<Insight>>()
                                             , It.IsAny<CancellationToken>()))
            .ReturnsAsync("woven response that mentions the suggestion");

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("hello"));

        Assert.Equal("woven response that mentions the suggestion", response.Message);
        Assert.Single(response.Insights);
        Assert.Equal("reflection.signal-1", response.Insights[0].DeduplicationKey);

        var context = _contextStore.GetOrCreate("test-session");
        Assert.Single(context.LastEmittedInsights);
        Assert.Equal("reflection.signal-1", context.LastEmittedInsights[0].DeduplicationKey);
    }

    [Fact]
    public async Task EngineEmitsZero_MessageIsRaw_AndInsightsArrayIsEmpty()
    {
        StubInterpreterReturns("DoSomething");
        StubRegistryReturns(SimpleAction());
        StubExecutionReturns("raw exec output");

        StubEngineEmits(/* none */);

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("hello"));

        Assert.Equal("raw exec output", response.Message);
        Assert.Empty(response.Insights);

        // Weave never fires when there are no insights to weave.
        _routerMock.Verify(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                                    , It.IsAny<string>()
                                                    , It.IsAny<IReadOnlyList<Insight>>()
                                                    , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    [Fact]
    public async Task WeaveThrows_MessageFallsBackToRaw_InsightsStillPopulated_WeaveFailedLogged()
    {
        StubInterpreterReturns("DoSomething");
        StubRegistryReturns(SimpleAction());
        StubExecutionReturns("raw exec output");

        var emitted = Insight("reflection.signal-2");
        StubEngineEmits(emitted);

        _routerMock
            .Setup(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                             , It.IsAny<string>()
                                             , It.IsAny<IReadOnlyList<Insight>>()
                                             , It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("router exploded"));

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("hello"));

        Assert.Equal("raw exec output", response.Message);
        Assert.Single(response.Insights);
        Assert.Equal("reflection.signal-2", response.Insights[0].DeduplicationKey);

        _activityLogMock.Verify(log => log.LogAsync(
                                    It.Is<ActivityEvent>(activityEvent =>
                                        activityEvent.ActivityType == InsightActivityTypes.WeaveFailed)
                                  , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

    // ====================================================================
    // ENH-08: TURN RECORDING WIRING
    // ====================================================================

    [Fact]
    public async Task InterpreterPath_RecordsTurnWithUnwovenMessage_BeforeEngine_AndReplacesAfterWeave()
    {
        StubInterpreterReturns("DoSomething");
        StubRegistryReturns(SimpleAction());
        StubExecutionReturns("raw exec output");

        // Capture the Turns snapshot at the moment the engine was called — this is the
        // load-bearing assertion: the engine must see the current turn (un-woven) by then.
        IReadOnlyList<ConversationTurn>? turnsAtEngineTime = null;
        _engineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .Callback<ConversationContext, CancellationToken>((context, _) =>
            {
                turnsAtEngineTime = context.Turns.ToList();
            })
            .ReturnsAsync(new[] { Insight("reflection.signal-3") });

        _routerMock
            .Setup(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                             , It.IsAny<string>()
                                             , It.IsAny<IReadOnlyList<Insight>>()
                                             , It.IsAny<CancellationToken>()))
            .ReturnsAsync("WOVEN");

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(Request("hi there"));

        Assert.NotNull(turnsAtEngineTime);
        var turnSeenByEngine = Assert.Single(turnsAtEngineTime!);
        Assert.Equal("hi there",        turnSeenByEngine.UserMessage);
        Assert.Equal("raw exec output", turnSeenByEngine.AssistantMessage);
        Assert.Equal(TurnPath.Interpreter, turnSeenByEngine.Path);
        Assert.Equal("DoSomething",     turnSeenByEngine.ActionName);
        Assert.True(turnSeenByEngine.Succeeded);

        // Final state: turn was REPLACED with the woven message.
        var context     = _contextStore.GetOrCreate("test-session");
        var finalTurn   = Assert.Single(context.Turns);
        Assert.Equal("WOVEN", finalTurn.AssistantMessage);
    }

    [Fact]
    public async Task InterpreterPath_NoInsights_RecordsTurnOnce_WithRawMessage()
    {
        StubInterpreterReturns("DoSomething");
        StubRegistryReturns(SimpleAction());
        StubExecutionReturns("raw exec output");

        StubEngineEmits(/* none */);

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(Request("hi there"));

        var context = _contextStore.GetOrCreate("test-session");
        var turn    = Assert.Single(context.Turns);
        Assert.Equal("raw exec output", turn.AssistantMessage);
        Assert.Equal(TurnPath.Interpreter, turn.Path);
    }

    [Fact]
    public async Task FastPath_RecordsTurn_WithFastPathStamp()
    {
        var action = SimpleAction("FastDo", isReplayable: true);

        // Set up FastPath to resolve.
        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns((string _
                    , out ActionMetadata? meta
                    , out Dictionary<string, string>? parameters) =>
            {
                meta       = action;
                parameters = new Dictionary<string, string>();
                return true;
            });

        StubExecutionReturns("fastpath result");

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(Request("just do it"));

        var context = _contextStore.GetOrCreate("test-session");
        var turn    = Assert.Single(context.Turns);
        Assert.Equal("just do it",       turn.UserMessage);
        Assert.Equal("fastpath result",  turn.AssistantMessage);
        Assert.Equal(TurnPath.FastPath,  turn.Path);
        Assert.Equal("FastDo",           turn.ActionName);
        Assert.True(turn.Succeeded);
    }

    [Fact]
    public async Task SuccessivelyRecordedTurns_AccumulateInOrder()
    {
        StubInterpreterReturns("DoSomething");
        StubRegistryReturns(SimpleAction());
        StubExecutionReturns("output A");
        StubEngineEmits(/* none */);

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(Request("first"));

        StubExecutionReturns("output B");
        await orchestrator.ConverseAsync(Request("second"));

        var context = _contextStore.GetOrCreate("test-session");
        Assert.Equal(2, context.Turns.Count);
        Assert.Equal("first",     context.Turns[0].UserMessage);
        Assert.Equal("output A",  context.Turns[0].AssistantMessage);
        Assert.Equal("second",    context.Turns[1].UserMessage);
        Assert.Equal("output B",  context.Turns[1].AssistantMessage);
    }

    // ====================================================================
    // ENH-09 / B.4: INSIGHT ENGINE ON FASTPATH TURNS
    // ====================================================================

    [Fact]
    public async Task FastPath_EngineEmitsInsights_MessageIsWoven_AndWeaveIsCalled()
    {
        var action = SimpleAction("FastDo", isReplayable: true);

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns((string _
                    , out ActionMetadata? meta
                    , out Dictionary<string, string>? parameters) =>
            {
                meta       = action;
                parameters = new Dictionary<string, string>();
                return true;
            });

        StubExecutionReturns("fastpath raw output");

        var emitted = Insight("journal.no-entry-today", "You haven't journaled today.");
        StubEngineEmits(emitted);

        _routerMock
            .Setup(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                             , It.IsAny<string>()
                                             , It.IsAny<IReadOnlyList<Insight>>()
                                             , It.IsAny<CancellationToken>()))
            .ReturnsAsync("fastpath woven response");

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("list tasks"));

        Assert.Equal("fastpath woven response", response.Message);
        Assert.Single(response.Insights);
        Assert.Equal("journal.no-entry-today", response.Insights[0].DeduplicationKey);

        _routerMock.Verify(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                                     , It.IsAny<string>()
                                                     , It.IsAny<IReadOnlyList<Insight>>()
                                                     , It.IsAny<CancellationToken>())
                         , Times.Once);
    }

    [Fact]
    public async Task FastPath_NonReplayableAction_SkipsInsightEngine_AndReturnsRawMessage()
    {
        var action = SimpleAction("FastReadOnly");

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns((string _
                    , out ActionMetadata? meta
                    , out Dictionary<string, string>? parameters) =>
            {
                meta       = action;
                parameters = new Dictionary<string, string>();
                return true;
            });

        StubExecutionReturns("fastpath raw output");

        var emitted = Insight("journal.no-entry-today", "You haven't journaled today.");
        StubEngineEmits(emitted);

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("list tasks"));

        Assert.Equal("fastpath raw output", response.Message);
        Assert.Empty(response.Insights);

        _engineMock.Verify(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                                , It.IsAny<CancellationToken>())
                          , Times.Never);
        _routerMock.Verify(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                                     , It.IsAny<string>()
                                                     , It.IsAny<IReadOnlyList<Insight>>()
                                                     , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    [Fact]
    public async Task FastPath_EngineEmitsZero_WeaveIsSkipped_RawMessageReturned()
    {
        var action = SimpleAction("FastDo", isReplayable: true);

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns((string _
                    , out ActionMetadata? meta
                    , out Dictionary<string, string>? parameters) =>
            {
                meta       = action;
                parameters = new Dictionary<string, string>();
                return true;
            });

        StubExecutionReturns("fastpath raw output");
        StubEngineEmits(/* none */);

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("list tasks"));

        Assert.Equal("fastpath raw output", response.Message);
        Assert.Empty(response.Insights);

        // Weave must not fire — FastPath turns with no insights skip the LLM call.
        _routerMock.Verify(router => router.WeaveAsync(It.IsAny<ConversationContext>()
                                                     , It.IsAny<string>()
                                                     , It.IsAny<IReadOnlyList<Insight>>()
                                                     , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    [Fact]
    public async Task FastPath_ReportBug_And_ReportIdea_SkipInsightEngine_Immediately()
    {
        var action = SimpleAction("ReportBug");

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                 , out It.Ref<ActionMetadata?>.IsAny
                                                 , out It.Ref<Dictionary<string, string>?>.IsAny))
            .Returns((string _
                    , out ActionMetadata? meta
                    , out Dictionary<string, string>? parameters) =>
            {
                meta       = action;
                parameters = new Dictionary<string, string> { ["description"] = "Test bug description" };
                return true;
            });

        StubExecutionReturns("Bug logged ✓ — added to `Bug Log.md` with ID `1234`.");

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(Request("Bug: Test bug description"));

        Assert.Equal("Bug logged ✓ — added to `Bug Log.md` with ID `1234`.", response.Message);
        Assert.Empty(response.Insights);

        // Insight engine must NOT fire for feedback actions
        _engineMock.Verify(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                                , It.IsAny<CancellationToken>())
                          , Times.Never);
    }
}
