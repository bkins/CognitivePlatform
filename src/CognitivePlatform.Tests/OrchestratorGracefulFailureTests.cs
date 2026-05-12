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

namespace CognitivePlatform.Tests;

[Collection("LlmSharedState")]
/// <summary>
/// EPIC-08 B2: Verifies that the orchestrator returns improved user-friendly messages
/// for the no-action-recognized and missing-parameters failure paths, replacing the
/// generic "I didn't recognize that" and "I'm missing some required details" strings.
/// </summary>
public class OrchestratorGracefulFailureTests
{
    private readonly Mock<IActionRegistry>   _registryMock    = new();
    private readonly Mock<IInterpreter>      _interpreterMock = new();
    private readonly Mock<IExecutionEngine>  _executionMock   = new();
    private readonly Mock<IFastPathResolver> _fastPathMock    = new();
    private readonly Mock<ILlmRouter>        _routerMock      = new();
    private readonly Mock<IIdempotencyStore> _idempotencyMock = new();
    private readonly Mock<IInsightEngine>    _engineMock      = new();
    private readonly Mock<IActivityLog>      _activityLogMock = new();
    private readonly Mock<ITelemetrySink>    _telemetryMock   = new();
    private readonly Mock<ILlmRateLimiter>   _rateLimiterMock = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new();

    public OrchestratorGracefulFailureTests()
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
    }

    private ConversationOrchestrator BuildOrchestrator() =>
        new(_registryMock.Object
          , _interpreterMock.Object
          , _executionMock.Object
          , _contextStore
          , _telemetryMock.Object
          , _fastPathMock.Object
          , _routerMock.Object
          , _idempotencyMock.Object
          , _telemetryContext
          , _engineMock.Object
          , _historyStore
          , _activityLogMock.Object
          , _modelCatalog
          , _providerDefaults
          , _rateLimiterMock.Object);

    // ================================================================
    // NO ACTION RECOGNIZED
    // ================================================================

    [Fact]
    public async Task ConverseAsync_IncludesCandidateName_WhenNoActionWithCandidates()
    {
        _registryMock.SetupGet(registry => registry.Actions)
                     .Returns(Array.Empty<ActionMetadata>());

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                  , It.IsAny<ConversationContext>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = null
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.NoMatchingAction
                                , CandidateActions    = new List<string> { "AddTask" }
                                , Reason              = "Input unclear."
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "do something"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("AddTask", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConverseAsync_SuggestsHelpCommand_WhenNoActionRecognized()
    {
        _registryMock.SetupGet(registry => registry.Actions)
                     .Returns(Array.Empty<ActionMetadata>());

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                  , It.IsAny<ConversationContext>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = null
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.NoMatchingAction
                                , CandidateActions    = null
                                , Reason              = "No match found."
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "xyzzy"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("what can you do", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================
    // MISSING PARAMETERS (no clarification path)
    // ================================================================

    [Fact]
    public async Task ConverseAsync_IncludesMissingParamName_WhenActionDoesNotAllowClarification()
    {
        var action = new ActionMetadata
                     {
                             Name                = "AddTask"
                           , AllowsClarification = false
                           , Parameters          = new List<ParameterMetadata>
                                                   {
                                                       new()
                                                       {
                                                               Name       = "shortDescription"
                                                             , IsOptional = false
                                                             , AllowEmpty = false
                                                       }
                                                   }
                     };

        _registryMock.SetupGet(registry => registry.Actions)
                     .Returns(new[] { action });

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                  , It.IsAny<ConversationContext>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = "AddTask"
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.MissingParameters
                                , MissingParameters   = new List<string> { "shortDescription" }
                                , Reason              = "Missing required parameter: shortDescription"
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "add a task"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("shortDescription", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConverseAsync_ReturnsRetryGuidance_WhenMissingParameters_NoClarification()
    {
        var action = new ActionMetadata
                     {
                             Name                = "AddTask"
                           , AllowsClarification = false
                           , Parameters          = new List<ParameterMetadata>
                                                   {
                                                       new()
                                                       {
                                                               Name       = "shortDescription"
                                                             , IsOptional = false
                                                             , AllowEmpty = false
                                                       }
                                                   }
                     };

        _registryMock.SetupGet(registry => registry.Actions)
                     .Returns(new[] { action });

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                  , It.IsAny<ConversationContext>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = "AddTask"
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.MissingParameters
                                , MissingParameters   = new List<string> { "shortDescription" }
                                , Reason              = "Missing required parameter: shortDescription"
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "add a task"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("missing", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
