using Moq;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
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

[Collection("LlmSharedState")]
public class PhaseE_OrchestratorTopologyTests
{
    private readonly Mock<ICapabilityRegistry>       _registryMock         = new();
    private readonly Mock<IInterpreter>             _interpreterMock      = new();
    private readonly Mock<IExecutionEngine>         _executionMock        = new();
    private readonly Mock<IFastPathResolver>        _fastPathMock         = new();
    private readonly Mock<ILlmRouter>               _routerMock           = new();
    private readonly Mock<IIdempotencyStore>        _idempotencyMock      = new();
    private readonly Mock<IInsightEngine>           _engineMock           = new();
    private readonly Mock<IActivityLog>             _activityLogMock      = new();
    private readonly Mock<ITelemetrySink>           _telemetryMock        = new();
    private readonly Mock<ILlmRateLimiter>          _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore>   _turnStoreMock        = new();
    private readonly Mock<IWorkspaceContext>        _workspaceContextMock = new();
    private readonly Mock<IPersonaRuntime>          _personaRuntimeMock   = new();
    private readonly Mock<IPersonaSessionManager>   _sessionManagerMock   = new();
    private readonly Mock<IEmotionalTopologyTracker> _topologyTrackerMock  = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "topology-test" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new();

    private const string SessionId = "topology-test";

    public PhaseE_OrchestratorTopologyTests()
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

        _engineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Insight>)Array.Empty<Insight>());

        _rateLimiterMock
            .Setup(limiter => limiter.GetCurrentSnapshot(It.IsAny<string>()))
            .Returns(LlmRateLimitSnapshot.Empty);

        _registryMock
            .Setup(registry => registry.GetAll())
            .Returns(Array.Empty<ActionMetadata>());

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                 , It.IsAny<ConversationContext>()
                                                                 , It.IsAny<TaskComplexity>()))
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        _sessionManagerMock
            .Setup(manager => manager.IsPersonaConversation(SessionId))
            .Returns(true);

        _sessionManagerMock
            .Setup(manager => manager.GetActivePersona(SessionId))
            .Returns(Guid.NewGuid());

        _personaRuntimeMock
            .Setup(runtime => runtime.BuildConversationContextAsync(It.IsAny<Guid>()
                                                                   , It.IsAny<string>()
                                                                   , It.IsAny<ConversationMode>()
                                                                   , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaConversationContext
                          {
                              PersonaId    = Guid.NewGuid()
                            , PersonaName  = "Sarah"
                            , SystemPrompt = "You are embodying Sarah."
                          });

        _routerMock
            .Setup(router => router.SendAsync(It.IsAny<string>()
                                            , It.IsAny<ConversationContext>()
                                            , It.IsAny<TaskComplexity>()
                                            , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "I miss those days." });

        _topologyTrackerMock
            .Setup(tracker => tracker.RecordSampleAsync(It.IsAny<EmotionalTopologyPoint>()
                                                      , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ConverseAsync_RecordsTopologySample_WhenPersonaSessionActive()
    {
        var orchestrator = BuildOrchestrator();

        await orchestrator.ConverseAsync(new ConverseRequest { Input = "I miss her.", SessionId = SessionId });

        _topologyTrackerMock.Verify(tracker => tracker.RecordSampleAsync(
                                        It.Is<EmotionalTopologyPoint>(point => point.PersonaId != Guid.Empty)
                                      , It.IsAny<CancellationToken>())
                                  , Times.Once);
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
          , new TaskComplexityClassifier()
          , personaEngine:            null
          , personaRuntime:           _personaRuntimeMock.Object
          , personaSessionManager:    _sessionManagerMock.Object
          , emotionalTopologyTracker: _topologyTrackerMock.Object);
}
