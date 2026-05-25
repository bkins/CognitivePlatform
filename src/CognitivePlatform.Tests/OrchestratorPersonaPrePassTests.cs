using Moq;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;
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
public class OrchestratorPersonaPrePassTests
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
    private readonly Mock<IPersonaEngine>         _personaEngineMock    = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "persona-test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new();

    private const string SessionId = "persona-test-session";

    public OrchestratorPersonaPrePassTests()
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
    }

    // ================================================================
    // Persona applies model override when personality has a ModelConfig
    // ================================================================

    [Fact]
    public async Task ConverseAsync_AppliesPersonalityModel_WhenPersonaEngineResolvesPersonality()
    {
        var personalityWithModel = new PersonalityDefinition
                                   {
                                       Id          = "tech-persona"
                                     , Name        = "TechnicalHelper"
                                     , ModelConfig = new PersonalityModelConfig { ModelId = "gemma-3b-it" }
                                   };

        _personaEngineMock
            .Setup(engine => engine.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaContextResult
                          {
                              Intent               = Intent.TechnicalHelp
                            , Personality          = personalityWithModel
                            , IntentAnalysisResult = new IntentAnalysisResult
                                                     {
                                                         Intent     = Intent.TechnicalHelp
                                                       , Confidence = 0.9
                                                     }
                          });

        ConversationContext? capturedContext = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator(personaEngine: _personaEngineMock.Object);
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "fix my bug", SessionId = SessionId });

        Assert.NotNull(capturedContext);
        Assert.True(capturedContext!.Metadata.TryGetValue("model", out var appliedModel));
        Assert.Equal("gemma-3b-it", appliedModel);
    }

    // ================================================================
    // Persona does not override when Intent.Unknown
    // ================================================================

    [Fact]
    public async Task ConverseAsync_LeavesModelUnchanged_WhenPersonaEngineResolvesUnknownIntent()
    {
        _personaEngineMock
            .Setup(engine => engine.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaContextResult
                          {
                              Intent               = Intent.Unknown
                            , Personality          = null
                            , IntentAnalysisResult = new IntentAnalysisResult
                                                     {
                                                         Intent     = Intent.Unknown
                                                       , Confidence = 0.0
                                                     }
                          });

        ConversationContext? capturedContext = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator(personaEngine: _personaEngineMock.Object);
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "just chatting", SessionId = SessionId });

        Assert.NotNull(capturedContext);
        Assert.False(capturedContext!.Metadata.ContainsKey("model"),
                     "Model should not be set when persona engine returns Intent.Unknown");
    }

    // ================================================================
    // No persona engine (null) — existing flow unchanged
    // ================================================================

    [Fact]
    public async Task ConverseAsync_ProceedsNormally_WhenPersonaEngineIsNull()
    {
        var orchestrator = BuildOrchestrator(personaEngine: null);
        var response = await orchestrator.ConverseAsync(
                           new ConverseRequest { Input = "hello", SessionId = SessionId });

        Assert.NotNull(response);
    }

    // ================================================================
    // Persona with no ModelConfig — model not overridden
    // ================================================================

    [Fact]
    public async Task ConverseAsync_LeavesModelUnchanged_WhenPersonalityHasNoModelConfig()
    {
        var personalityWithoutModel = new PersonalityDefinition
                                      {
                                          Id          = "basic-persona"
                                        , Name        = "General"
                                        , ModelConfig = null
                                      };

        _personaEngineMock
            .Setup(engine => engine.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaContextResult
                          {
                              Intent               = Intent.GeneralHelp
                            , Personality          = personalityWithoutModel
                            , IntentAnalysisResult = new IntentAnalysisResult
                                                     {
                                                         Intent     = Intent.GeneralHelp
                                                       , Confidence = 0.7
                                                     }
                          });

        ConversationContext? capturedContext = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator(personaEngine: _personaEngineMock.Object);
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "explain things", SessionId = SessionId });

        Assert.NotNull(capturedContext);
        Assert.False(capturedContext!.Metadata.ContainsKey("model"),
                     "Model should not be set when personality has no ModelConfig");
    }

    // ================================================================
    // Persona engine throws — turn completes normally (non-cancellation)
    // ================================================================

    [Fact]
    public async Task ConverseAsync_CompletesNormally_WhenPersonaEngineThrows()
    {
        _personaEngineMock
            .Setup(engine => engine.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Engine exploded"));

        var orchestrator = BuildOrchestrator(personaEngine: _personaEngineMock.Object);
        var response = await orchestrator.ConverseAsync(
                           new ConverseRequest { Input = "help me code", SessionId = SessionId });

        Assert.NotNull(response);
    }

    // ================================================================
    // Persona engine does not override when request already pins a model
    // ================================================================

    [Fact]
    public async Task ConverseAsync_DoesNotApplyPersonalityModel_WhenRequestHasPinnedModel()
    {
        var personalityWithModel = new PersonalityDefinition
                                   {
                                       Id          = "tech-persona"
                                     , Name        = "TechnicalHelper"
                                     , ModelConfig = new PersonalityModelConfig { ModelId = "gemma-3b-it" }
                                   };

        _personaEngineMock
            .Setup(engine => engine.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaContextResult
                          {
                              Intent               = Intent.TechnicalHelp
                            , Personality          = personalityWithModel
                            , IntentAnalysisResult = new IntentAnalysisResult
                                                     {
                                                         Intent     = Intent.TechnicalHelp
                                                       , Confidence = 0.9
                                                     }
                          });

        ConversationContext? capturedContext = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator(personaEngine: _personaEngineMock.Object);
        await orchestrator.ConverseAsync(new ConverseRequest
                                         {
                                             Input    = "fix my bug"
                                           , SessionId = SessionId
                                           , Model    = "explicit-pinned-model"
                                         });

        Assert.NotNull(capturedContext);
        Assert.True(capturedContext!.Metadata.TryGetValue("model", out var appliedModel));
        Assert.Equal("explicit-pinned-model", appliedModel);
    }

    // ================================================================
    // Cancellation propagates out of the persona pre-pass
    // ================================================================

    [Fact]
    public async Task ConverseAsync_PropagatesCancellation_WhenPersonaEngineIsCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _personaEngineMock
            .Setup(engine => engine.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var orchestrator = BuildOrchestrator(personaEngine: _personaEngineMock.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.ConverseAsync(new ConverseRequest { Input = "help me", SessionId = SessionId }
                                     , cts.Token));
    }

    // --- Helpers -----------------------------------------------------------------

    private ConversationOrchestrator BuildOrchestrator(IPersonaEngine? personaEngine) =>
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
          , personaEngine);
}
