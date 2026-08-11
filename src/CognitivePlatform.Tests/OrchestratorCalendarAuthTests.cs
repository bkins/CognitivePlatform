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
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CognitivePlatform.Tests;

public class OrchestratorCalendarAuthTests
{
    private readonly Mock<ICapabilityRegistry>   _registryMock         = new();
    private readonly Mock<IInterpreter>          _interpreterMock      = new();
    private readonly Mock<IExecutionEngine>      _executionMock        = new();
    private readonly Mock<IFastPathResolver>     _fastPathMock         = new();
    private readonly Mock<ILlmRouter>            _routerMock           = new();
    private readonly Mock<IIdempotencyStore>     _idempotencyMock      = new();
    private readonly Mock<IInsightEngine>        _engineMock           = new();
    private readonly Mock<IActivityLog>          _activityLogMock      = new();
    private readonly Mock<ITelemetrySink>        _telemetryMock        = new();
    private readonly Mock<ILlmRateLimiter>       _rateLimiterMock      = new();
    private readonly Mock<IConversationTurnStore> _turnStoreMock        = new();
    private readonly Mock<IWorkspaceContext>     _workspaceContextMock = new();

    private readonly InMemoryInsightHistoryStore _historyStore     = new();
    private readonly ConversationContextStore    _contextStore     = new();
    private readonly TelemetryContext            _telemetryContext = new() { SessionId = "auth-test-session" };
    private readonly LlmModelCatalog             _modelCatalog     = new();
    private readonly LlmProviderDefaults         _providerDefaults = new(Microsoft.Extensions.Options.Options.Create(new LlmClientSettings()));

    private const string SessionId = "auth-test-session";

    public OrchestratorCalendarAuthTests()
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

    [Fact]
    public async Task FinalizeAsync_SetsRequiresAuthAndUrls_WhenMetadataContainsAuthFlag()
    {
        var orchestrator = BuildOrchestrator();
        var context = _contextStore.GetOrCreate(SessionId);
        context.Metadata["requires_auth"] = "true";
        context.Metadata["auth_provider"] = "GoogleCalendar";
        context.Metadata["auth_url"]      = "http://localhost/auth";

        var request = new ConverseRequest { SessionId = SessionId, Input = "test" };
        var response = new ConverseResponse { Message = "Not connected" };

        var result = await orchestrator.FinalizeAsync(request, response, new System.Diagnostics.Stopwatch(), TurnPath.FastPath);

        Assert.True(result.RequiresAuth);
        Assert.Equal("GoogleCalendar", result.AuthProvider);
        Assert.Equal("http://localhost/auth", result.AuthUrl);

        // Verify clean-up
        Assert.False(context.Metadata.ContainsKey("requires_auth"));
        Assert.False(context.Metadata.ContainsKey("auth_provider"));
        Assert.False(context.Metadata.ContainsKey("auth_url"));
    }
}
