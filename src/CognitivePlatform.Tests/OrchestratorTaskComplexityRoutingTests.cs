using Moq;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.PersonaEngine;
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

/// <summary>
/// ENH-19 Phase B: end-to-end verification that the classifier's verdict reaches
/// IInterpreter.InterpretWithContext when ConverseAsync drops through to the
/// LLM interpreter path. FastPath turns must NOT classify (they never reach
/// the interpreter), so a stubbed classifier that throws would prove that.
/// </summary>
[Collection("LlmSharedState")]
public class OrchestratorTaskComplexityRoutingTests
{
    private readonly Mock<ICapabilityRegistry>  _registryMock         = new();
    private readonly Mock<IInterpreter>        _interpreterMock      = new();
    private readonly Mock<IExecutionEngine>    _executionMock        = new();
    private readonly Mock<IFastPathResolver>   _fastPathMock         = new();
    private readonly Mock<ILlmRouter>          _routerMock           = new();
    private readonly Mock<IIdempotencyStore>   _idempotencyMock      = new();
    private readonly Mock<IInsightEngine>      _engineMock           = new();
    private readonly Mock<IActivityLog>        _activityLogMock      = new();
    private readonly Mock<ITelemetrySink>      _telemetryMock        = new();
    private readonly Mock<ILlmRateLimiter>     _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore> _turnStoreMock     = new();
    private readonly Mock<IWorkspaceContext>   _workspaceContextMock = new();
    private readonly Mock<ITaskComplexityClassifier> _classifierMock = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "complexity-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new LlmProviderDefaults(Options.Create(new LlmClientSettings()));

    private const string SessionId = "complexity-session";

    public OrchestratorTaskComplexityRoutingTests()
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
    }

    [Fact]
    public async Task ConverseAsync_PassesClassifierResult_ToInterpreter_WhenHeavy()
    {
        _classifierMock.Setup(classifier => classifier.Classify(It.IsAny<string>()))
                       .Returns(TaskComplexity.Heavy);

        TaskComplexity? captured = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                 , It.IsAny<ConversationContext>()
                                                                 , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, _, complexity) => captured = complexity)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "deep dive on my mood", SessionId = SessionId });

        Assert.Equal(TaskComplexity.Heavy, captured);
    }

    [Fact]
    public async Task ConverseAsync_PassesClassifierResult_ToInterpreter_WhenLight()
    {
        _classifierMock.Setup(classifier => classifier.Classify(It.IsAny<string>()))
                       .Returns(TaskComplexity.Light);

        TaskComplexity? captured = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                 , It.IsAny<ConversationContext>()
                                                                 , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, _, complexity) => captured = complexity)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "hi", SessionId = SessionId });

        Assert.Equal(TaskComplexity.Light, captured);
    }

    [Fact]
    public async Task ConverseAsync_PassesClassifierResult_ToInterpreter_WhenStandard()
    {
        _classifierMock.Setup(classifier => classifier.Classify(It.IsAny<string>()))
                       .Returns(TaskComplexity.Standard);

        TaskComplexity? captured = null;
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                 , It.IsAny<ConversationContext>()
                                                                 , It.IsAny<TaskComplexity>()))
            .Callback<string, ConversationContext, TaskComplexity>((_, _, complexity) => captured = complexity)
            .ReturnsAsync(new InterpreterResult { ActionName = null });

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "schedule a meeting tomorrow", SessionId = SessionId });

        Assert.Equal(TaskComplexity.Standard, captured);
    }

    [Fact]
    public async Task ConverseAsync_InvokesClassifier_WithRequestInput()
    {
        _classifierMock.Setup(classifier => classifier.Classify(It.IsAny<string>()))
                       .Returns(TaskComplexity.Standard);

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "analyze last week",
                                                                SessionId = SessionId });

        _classifierMock.Verify(classifier => classifier.Classify("analyze last week"), Times.Once);
    }

    [Fact]
    public async Task ConverseAsync_DoesNotInvokeClassifier_WhenFastPathResolvesAction()
    {
        var fastAction = new ActionMetadata
                         {
                                 Name          = "ShowVersion"
                               , Description   = "Show version"
                               , IsDestructive = false
                         };

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>()
                                                  , out It.Ref<ActionMetadata?>.IsAny!
                                                  , out It.Ref<Dictionary<string, string>?>.IsAny!))
            .Returns((string _, out ActionMetadata? action, out Dictionary<string, string>? parameters) =>
                     {
                         action     = fastAction;
                         parameters = new Dictionary<string, string>();
                         return true;
                     });

        _executionMock
            .Setup(engine => engine.ExecuteAsync(It.IsAny<ActionMetadata>()
                                                , It.IsAny<IDictionary<string, string>>()
                                                , It.IsAny<string>()
                                                , It.IsAny<CancellationToken>()))
            .ReturnsAsync("Version 1.0");

        _classifierMock.Setup(classifier => classifier.Classify(It.IsAny<string>()))
                       .Returns(TaskComplexity.Standard);

        var orchestrator = BuildOrchestrator();
        await orchestrator.ConverseAsync(new ConverseRequest { Input = "version", SessionId = SessionId });

        _classifierMock.Verify(classifier => classifier.Classify(It.IsAny<string>()), Times.Never);
    }

    // --- Helpers --------------------------------------------------------

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
          , _classifierMock.Object);
}
