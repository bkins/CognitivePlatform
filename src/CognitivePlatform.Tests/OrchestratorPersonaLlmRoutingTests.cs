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
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;

[Collection("LlmSharedState")]
public class OrchestratorPersonaLlmRoutingTests
{
    private readonly Mock<IActionRegistry>        _registryMock         = new();
    private readonly Mock<IInterpreter>           _interpreterMock      = new();
    private readonly Mock<IExecutionEngine>       _executionMock        = new();
    private readonly Mock<IFastPathResolver>      _fastPathMock         = new();
    private readonly Mock<ILlmRouter>             _routerMock           = new();
    private readonly Mock<IIdempotencyStore>      _idempotencyMock      = new();
    private readonly Mock<IInsightEngine>         _engineMock           = new();
    private readonly Mock<IActivityLog>           _activityLogMock      = new();
    private readonly Mock<ITelemetrySink>         _telemetryMock        = new();
    private readonly Mock<ILlmRateLimiter>        _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore> _turnStoreMock        = new();
    private readonly Mock<IWorkspaceContext>      _workspaceContextMock = new();
    private readonly Mock<IPersonaRuntime>        _personaRuntimeMock   = new();
    private readonly Mock<IPersonaSessionManager> _sessionManagerMock   = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "persona-routing-test" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new();

    private const string SessionId         = "persona-routing-test";
    private const string DefaultSystemPrompt = "You are modeling Sarah, a warm and thoughtful person.";

    public OrchestratorPersonaLlmRoutingTests()
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
            .SetupGet(registry => registry.Actions)
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
                            , SystemPrompt = DefaultSystemPrompt
                          });

        _routerMock
            .Setup(router => router.SendAsync(It.IsAny<string>()
                                            , It.IsAny<ConversationContext>()
                                            , It.IsAny<TaskComplexity>()
                                            , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "Hello, it's Sarah speaking." });
    }

    // ================================================================
    // Routes to LLM when persona mode is active and interpreter finds no action
    // ================================================================

    [Fact]
    public async Task ConverseAsync_RoutesToLlm_WhenPersonaModeActiveAndNoActionRecognized()
    {
        var orchestrator = BuildOrchestrator();

        var response = await orchestrator.ConverseAsync(
                           new ConverseRequest { Input = "How are you?", SessionId = SessionId });

        Assert.Equal("Hello, it's Sarah speaking.", response.Message);
        _routerMock.Verify(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>())
                         , Times.Once);
    }

    // ================================================================
    // No LLM call when persona mode is not active and no action found
    // ================================================================

    [Fact]
    public async Task ConverseAsync_ReturnsNoActionMessage_WhenNoPersonaModeAndNoActionRecognized()
    {
        _sessionManagerMock
            .Setup(manager => manager.IsPersonaConversation(SessionId))
            .Returns(false);

        var orchestrator = BuildOrchestrator();

        var response = await orchestrator.ConverseAsync(
                           new ConverseRequest { Input = "blorp zap", SessionId = SessionId });

        Assert.NotEqual("Hello, it's Sarah speaking.", response.Message);
        _routerMock.Verify(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    // ================================================================
    // System prompt is prepended to the LLM call in persona mode
    // ================================================================

    [Fact]
    public async Task ConverseAsync_PrependsSytemPromptToLlmPrompt_WhenPersonaModeActive()
    {
        string? capturedPrompt = null;
        _routerMock
            .Setup(router => router.SendAsync(It.IsAny<string>()
                                            , It.IsAny<ConversationContext>()
                                            , It.IsAny<TaskComplexity>()
                                            , It.IsAny<CancellationToken>()))
            .Callback<string, ConversationContext, TaskComplexity, CancellationToken>((prompt, _, _, _) => capturedPrompt = prompt)
            .ReturnsAsync(new LlmResponse { Content = "In character response." });

        var orchestrator = BuildOrchestrator();

        await orchestrator.ConverseAsync(
            new ConverseRequest { Input = "Tell me about yourself.", SessionId = SessionId });

        Assert.NotNull(capturedPrompt);
        Assert.Contains(DefaultSystemPrompt,        capturedPrompt);
        Assert.Contains("Tell me about yourself.", capturedPrompt);
    }

    // ================================================================
    // Memories are included in the LLM prompt when persona has memories
    // ================================================================

    [Fact]
    public async Task ConverseAsync_IncludesMemoriesInLlmPrompt_WhenPersonaHasMemories()
    {
        var memories = new List<PersonaMemory>
                       {
                           new() { Content = "She loved hiking in the mountains." }
                         , new() { Content = "She was afraid of thunderstorms." }
                       };

        _personaRuntimeMock
            .Setup(runtime => runtime.BuildConversationContextAsync(It.IsAny<Guid>()
                                                                   , It.IsAny<string>()
                                                                   , It.IsAny<ConversationMode>()
                                                                   , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaConversationContext
                          {
                              PersonaId        = Guid.NewGuid()
                            , PersonaName      = "Sarah"
                            , SystemPrompt     = DefaultSystemPrompt
                            , RelevantMemories = memories
                          });

        string? capturedPrompt = null;
        _routerMock
            .Setup(router => router.SendAsync(It.IsAny<string>()
                                            , It.IsAny<ConversationContext>()
                                            , It.IsAny<TaskComplexity>()
                                            , It.IsAny<CancellationToken>()))
            .Callback<string, ConversationContext, TaskComplexity, CancellationToken>((prompt, _, _, _) => capturedPrompt = prompt)
            .ReturnsAsync(new LlmResponse { Content = "Memory-aware response." });

        var orchestrator = BuildOrchestrator();

        await orchestrator.ConverseAsync(
            new ConverseRequest { Input = "Do you like hiking?", SessionId = SessionId });

        Assert.NotNull(capturedPrompt);
        Assert.Contains("She loved hiking in the mountains.", capturedPrompt);
        Assert.Contains("She was afraid of thunderstorms.",   capturedPrompt);
    }

    // ================================================================
    // LLM response content becomes the turn message
    // ================================================================

    [Fact]
    public async Task ConverseAsync_ReturnsLlmContent_AsTurnMessage_WhenPersonaModeActive()
    {
        _routerMock
            .Setup(router => router.SendAsync(It.IsAny<string>()
                                            , It.IsAny<ConversationContext>()
                                            , It.IsAny<TaskComplexity>()
                                            , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "This is Sarah's in-character reply." });

        var orchestrator = BuildOrchestrator();

        var response = await orchestrator.ConverseAsync(
                           new ConverseRequest { Input = "Say something.", SessionId = SessionId });

        Assert.Equal("This is Sarah's in-character reply.", response.Message);
    }

    // ================================================================
    // InjectPersonaContext does not throw when persona runtime raises non-cancellation exception
    // ================================================================

    [Fact]
    public async Task ConverseAsync_ContinuesNormally_WhenPersonaRuntimeThrowsNonCancellation()
    {
        _personaRuntimeMock
            .Setup(runtime => runtime.BuildConversationContextAsync(It.IsAny<Guid>()
                                                                   , It.IsAny<string>()
                                                                   , It.IsAny<ConversationMode>()
                                                                   , It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Persona not found"));

        var orchestrator = BuildOrchestrator();

        var response = await orchestrator.ConverseAsync(
                           new ConverseRequest { Input = "hello", SessionId = SessionId });

        Assert.NotNull(response);
    }

    // --- Helpers -----------------------------------------------------------------

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
          , personaEngine:        null
          , personaRuntime:       _personaRuntimeMock.Object
          , personaSessionManager: _sessionManagerMock.Object);
}
