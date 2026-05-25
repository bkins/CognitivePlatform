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
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;

/// <summary>
/// ENH-19: verifies that ConversationOrchestrator surfaces a user-facing note
/// when a Heavy LLM request was served by a lower-tier model.
/// </summary>
[Collection("LlmSharedState")]
public class OrchestratorTierDowngradeTests
{
    private readonly Mock<IActionRegistry>   _registryMock         = new();
    private readonly Mock<IInterpreter>      _interpreterMock      = new();
    private readonly Mock<IExecutionEngine>  _executionMock        = new();
    private readonly Mock<IFastPathResolver> _fastPathMock         = new();
    private readonly Mock<ILlmRouter>        _routerMock           = new();
    private readonly Mock<IIdempotencyStore> _idempotencyMock      = new();
    private readonly Mock<IInsightEngine>    _engineMock           = new();
    private readonly Mock<IActivityLog>      _activityLogMock      = new();
    private readonly Mock<ITelemetrySink>    _telemetryMock        = new();
    private readonly Mock<ILlmRateLimiter>        _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore> _turnStoreMock        = new();
    private readonly Mock<IWorkspaceContext>      _workspaceContextMock = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "tier-test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new();

    private const string SessionId = "tier-test-session";

    public OrchestratorTierDowngradeTests()
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

        _engineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                         , It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Insight>)Array.Empty<Insight>());
    }

    private ConversationOrchestrator BuildOrchestrator()
        => new(_registryMock.Object
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

    private void StubInterpreterReturnsAction(string actionName)
        => _interpreterMock
               .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                      , It.IsAny<ConversationContext>()
                                                                                                                                      , It.IsAny<TaskComplexity>()))
               .ReturnsAsync(new InterpreterResult
                             {
                                     ActionName  = actionName
                                   , DebugInfo   = "stub"
                                   , FailureType = InterpreterFailureType.None
                             });

    private void StubRegistryWithAction(string actionName)
    {
        var action = new ActionMetadata { Name = actionName, Category = "Identity" };
        _registryMock.SetupGet(registry => registry.Actions).Returns(new[] { action });
    }

    private void StubExecutionReturns(string output)
        => _executionMock
               .Setup(exec => exec.ExecuteAsync(It.IsAny<ActionMetadata>()
                                              , It.IsAny<IDictionary<string, string>>()
                                              , It.IsAny<string>()
                                              , It.IsAny<CancellationToken>()))
               .ReturnsAsync(output);

    // ----------------------------------------------------------------
    // Tier downgrade note surface
    // ----------------------------------------------------------------

    [Fact]
    public async Task ConverseAsync_AppendsTierDowngradeNote_WhenContextCarriesDowngradeNote()
    {
        // Simulate what the LlmRouter does when a Heavy call is downgraded:
        // it stores the note in context.Metadata before the orchestrator checks.
        var context = _contextStore.GetOrCreate(SessionId);
        context.Metadata["tier_downgrade_note"] = "Note: the model best suited for this analysis "
            + "isn't available right now. Results may be less detailed than usual — try again later for the best output.";

        StubInterpreterReturnsAction("GenerateInsights");
        StubRegistryWithAction("GenerateInsights");
        StubExecutionReturns("Insights generated.");

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = SessionId
                                                                  , Input     = "generate insights"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("model best suited", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConverseAsync_NoDowngradeNote_WhenContextHasNoNote()
    {
        // No tier_downgrade_note in context — normal execution path.
        StubInterpreterReturnsAction("GenerateInsights");
        StubRegistryWithAction("GenerateInsights");
        StubExecutionReturns("Insights generated.");

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = SessionId
                                                                  , Input     = "generate insights"
                                                            });

        Assert.NotNull(response.Message);
        Assert.DoesNotContain("model best suited", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
