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

/// <summary>
/// BUG-20: Verifies that when the interpreter returns an exception carrying
/// a 429 status code, the orchestrator returns a user-friendly rate-limit
/// message with the reset time rather than the generic error response.
/// </summary>
[Collection("LlmSharedState")]
public class OrchestratorRateLimitTests
{
    private readonly Mock<ICapabilityRegistry>   _registryMock    = new();
    private readonly Mock<IInterpreter>      _interpreterMock = new();
    private readonly Mock<IExecutionEngine>  _executionMock   = new();
    private readonly Mock<IFastPathResolver> _fastPathMock    = new();
    private readonly Mock<ILlmRouter>        _routerMock      = new();
    private readonly Mock<IIdempotencyStore> _idempotencyMock = new();
    private readonly Mock<IInsightEngine>    _engineMock      = new();
    private readonly Mock<IActivityLog>      _activityLogMock = new();
    private readonly Mock<ITelemetrySink>    _telemetryMock   = new();
    private readonly Mock<ILlmRateLimiter>        _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore> _turnStoreMock        = new();
    private readonly Mock<IWorkspaceContext>      _workspaceContextMock = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new LlmProviderDefaults(Options.Create(new LlmClientSettings()));

    public OrchestratorRateLimitTests()
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

    [Fact]
    public async Task ConverseAsync_ReturnsRateLimitMessage_When429ExceptionOccurs()
    {
        var resetAt  = DateTimeOffset.UtcNow.AddMinutes(2);
        var snapshot = new LlmRateLimitSnapshot
                       {
                               Provider         = "Groq"
                             , RequestLimit      = 100
                             , RequestsRemaining = 0
                             , RequestsResetAt   = resetAt
                             , RequestsResetRaw  = "2m"
                       };

        _rateLimiterMock
            .Setup(limiter => limiter.GetLatest("Groq"))
            .Returns(snapshot);

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = null
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.Exception
                                , Exception           = new HttpRequestException("Groq API returned 429: rate limit exceeded")
                                , Reason              = "LLM call failed"
                                , DebugInfo           = "429 rate limit"
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "hello"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("rate limit", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resets at",  response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConverseAsync_ReturnsGenericMessage_WhenExceptionIsNot429()
    {
        _rateLimiterMock
            .Setup(limiter => limiter.GetLatest(It.IsAny<string>()))
            .Returns(LlmRateLimitSnapshot.Empty);

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = null
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.Exception
                                , Exception           = new HttpRequestException("Connection refused")
                                , Reason              = "LLM call failed"
                                , DebugInfo           = "connection error"
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "hello"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("went wrong", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConverseAsync_Returns429FallbackMessage_WhenResetTimeUnavailable()
    {
        _rateLimiterMock
            .Setup(limiter => limiter.GetLatest("Groq"))
            .Returns(LlmRateLimitSnapshot.Empty);

        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = null
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.Exception
                                , Exception           = new HttpRequestException("Groq API returned 429: rate limit exceeded")
                                , Reason              = "LLM call failed"
                                , DebugInfo           = "429"
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "hello"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("rate limit", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConverseAsync_ReturnsCapacityExhaustedMessage_WhenAllProvidersExhausted()
    {
        _interpreterMock
            .Setup(interpreter => interpreter.InterpretWithContext(It.IsAny<string>()
                                                                                                                                    , It.IsAny<ConversationContext>()
                                                                                                                                    , It.IsAny<TaskComplexity>()))
            .ReturnsAsync(new InterpreterResult
                          {
                                  ActionName          = null
                                , ExtractedParameters = new()
                                , FailureType         = InterpreterFailureType.Exception
                                , Exception           = new LlmCapacityExceededException()
                                , Reason              = "All providers exhausted"
                                , DebugInfo           = "capacity exceeded"
                          });

        var orchestrator = BuildOrchestrator();
        var response     = await orchestrator.ConverseAsync(new ConverseRequest
                                                            {
                                                                    SessionId = "test-session"
                                                                  , Input     = "hello"
                                                            });

        Assert.NotNull(response.Message);
        Assert.Contains("capacity", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
