using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Knowledge;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Insights.Models;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class OrchestratorGroundedRAGTests
{
    private readonly Mock<ICapabilityRegistry> _registryMock = new();
    private readonly Mock<IInterpreter> _interpreterMock = new();
    private readonly Mock<IExecutionEngine> _executionMock = new();
    private readonly Mock<IFastPathResolver> _fastPathMock = new();
    private readonly Mock<ILlmRouter> _routerMock = new();
    private readonly Mock<IIdempotencyStore> _idempotencyMock = new();
    private readonly Mock<IInsightEngine> _engineMock = new();
    private readonly Mock<IActivityLog> _activityLogMock = new();
    private readonly Mock<ITelemetrySink> _telemetryMock = new();
    private readonly Mock<ILlmRateLimiter> _rateLimiterMock = new();
    private readonly Mock<IConversationTurnStore> _turnStoreMock = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();
    private readonly Mock<IConversationMetadataStore> _metadataStoreMock = new();
    private readonly Mock<ITaskComplexityClassifier> _complexityClassifierMock = new();

    private readonly Mock<IKnowledgeIngestionService> _knowledgeIngestionServiceMock = new();

    private readonly InMemoryInsightHistoryStore _historyStore = new();
    private readonly ConversationContextStore _contextStore = new();
    private readonly TelemetryContext _telemetryContext = new() { SessionId = "rag-test-session" };
    private readonly LlmModelCatalog _modelCatalog = new();
    private readonly LlmProviderDefaults _providerDefaults = new(Options.Create(new LlmClientSettings()));

    private const string SessionId = "rag-test-session";

    public OrchestratorGroundedRAGTests()
    {
        _idempotencyMock
            .Setup(store => store.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConverseResponse?)null);

        _fastPathMock
            .Setup(resolver => resolver.TryResolve(It.IsAny<string>(), out It.Ref<ActionMetadata?>.IsAny, out It.Ref<Dictionary<string, string>?>.IsAny))
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
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Insight>)Array.Empty<Insight>());

        _complexityClassifierMock
            .Setup(c => c.Classify(It.IsAny<string>()))
            .Returns(TaskComplexity.Standard);
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
             , _metadataStoreMock.Object
             , _complexityClassifierMock.Object
             , knowledgeIngestionService: _knowledgeIngestionServiceMock.Object);

    [Fact]
    public async Task ChitChat_Under_Strict_DomainMode_WithEmptyContext_Returns_UNKNOWN()
    {
        var orchestrator = BuildOrchestrator();
        var context = _contextStore.GetOrCreate(SessionId);
        context.Metadata["active_knowledge_domain"] = "HR";

        var domain = new KnowledgeDomain
        {
            Name = "HR",
            Mode = KnowledgeDomainMode.Strict
        };

        _knowledgeIngestionServiceMock.Setup(k => k.GetDomainAsync("HR")).ReturnsAsync(domain);
        _knowledgeIngestionServiceMock.Setup(k => k.RetrieveContextAsync("HR", "hello", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>()); // empty

        _interpreterMock.Setup(i => i.InterpretWithContext("hello", context, TaskComplexity.Standard))
            .ReturnsAsync(new InterpreterResult
            {
                ActionName = "ChitChat",
                FailureType = InterpreterFailureType.None
            });

        var request = new ConverseRequest
        {
            SessionId = SessionId,
            Input = "hello"
        };

        var response = await orchestrator.ConverseAsync(request);

        Assert.Equal("UNKNOWN", response.Message);
        _routerMock.Verify(r => r.SendAsync(It.IsAny<string>(), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChitChat_Under_Strict_DomainMode_WithContext_Calls_LLMRouter_With_GroundedPrompt()
    {
        var orchestrator = BuildOrchestrator();
        var context = _contextStore.GetOrCreate(SessionId);
        context.Metadata["active_knowledge_domain"] = "HR";

        var domain = new KnowledgeDomain
        {
            Name = "HR",
            Mode = KnowledgeDomainMode.Strict
        };

        var entry = new VectorEntry
        {
            Id = "k:hr:1",
            Domain = "knowledge:hr",
            ReferenceId = Guid.NewGuid().ToString(),
            Text = "HR policy: PTO is 20 days per year."
        };

        _knowledgeIngestionServiceMock.Setup(k => k.GetDomainAsync("HR")).ReturnsAsync(domain);
        _knowledgeIngestionServiceMock.Setup(k => k.RetrieveContextAsync("HR", "how many days of PTO?", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult> { new(entry, 0.95f) });

        _interpreterMock.Setup(i => i.InterpretWithContext("how many days of PTO?", context, TaskComplexity.Standard))
            .ReturnsAsync(new InterpreterResult
            {
                ActionName = "ChitChat",
                FailureType = InterpreterFailureType.None
            });

        _routerMock.Setup(r => r.SendAsync(It.IsAny<string>(), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = "You get 20 days." });

        var request = new ConverseRequest
        {
            SessionId = SessionId,
            Input = "how many days of PTO?"
        };

        var response = await orchestrator.ConverseAsync(request);

        Assert.Equal("You get 20 days.", response.Message);
        _routerMock.Verify(r => r.SendAsync(It.IsAny<string>(), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
