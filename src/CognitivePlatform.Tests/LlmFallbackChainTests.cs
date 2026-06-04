using System.Net;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.SystemPromptLogging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// LlmFallbackChain — health-check / viable-entries logic
// ─────────────────────────────────────────────────────────────────────────────

public class LlmFallbackChainTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static LlmFallbackChain Build(bool enabled, IHttpClientFactory factory, params LlmFallbackEntry[] entries)
    {
        var settings = Options.Create(new LlmFallbackSettings
                                      {
                                              Enabled = enabled
                                            , Chain   = new List<LlmFallbackEntry>(entries)
                                      });
        return new LlmFallbackChain(settings, factory);
    }

    private static IHttpClientFactory FactoryReturning(HttpStatusCode code)
    {
        var client = new HttpClient(new FakeHttpHandler(_ => new HttpResponseMessage(code)));
        var mock   = new Mock<IHttpClientFactory>();
        mock.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(client);
        return mock.Object;
    }

    private static IHttpClientFactory FactoryThrowing(Exception ex)
    {
        var client = new HttpClient(new FakeHttpHandler(_ => throw ex));
        var mock   = new Mock<IHttpClientFactory>();
        mock.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(client);
        return mock.Object;
    }

    // ── GetViableFallbacksAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetViableFallbacksAsync_ReturnsEntry_WhenNoHealthCheckRequired()
    {
        var factory = new Mock<IHttpClientFactory>().Object;
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "Groq", Model = "llama-3.1-8b-instant" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Single(result);
        Assert.Equal(LlmProvider.Groq,       result[0].Provider);
        Assert.Equal("llama-3.1-8b-instant", result[0].Model);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_IncludesEntry_WhenHealthCheckSucceeds()
    {
        var factory = FactoryReturning(HttpStatusCode.OK);
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "Ollama", Model = "phi3:mini", HealthCheckUrl = "http://localhost:11434/api/tags" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Single(result);
        Assert.Equal(LlmProvider.Ollama, result[0].Provider);
        Assert.Equal("phi3:mini",        result[0].Model);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_SkipsEntry_WhenHealthCheckReturnsNonSuccess()
    {
        var factory = FactoryReturning(HttpStatusCode.ServiceUnavailable);
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "Ollama", Model = "phi3:mini", HealthCheckUrl = "http://localhost:11434/api/tags" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_SkipsEntry_WhenHealthCheckThrows()
    {
        var factory = FactoryThrowing(new HttpRequestException("connection refused"));
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "Ollama", Model = "phi3:mini", HealthCheckUrl = "http://localhost:11434/api/tags" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_SkipsEntry_WhenProviderNameIsUnrecognised()
    {
        var factory = new Mock<IHttpClientFactory>().Object;
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "UnknownProvider", Model = "some-model" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_ReturnsEmpty_WhenChainIsEmpty()
    {
        var factory = new Mock<IHttpClientFactory>().Object;
        var chain   = Build(enabled: true, factory);

        var result = await chain.GetViableFallbacksAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_ReturnsBothEntries_WhenFirstNeedsNoCheckAndSecondPasses()
    {
        var factory = FactoryReturning(HttpStatusCode.OK);
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "Groq",   Model = "llama-3.1-8b-instant" }
                          , new LlmFallbackEntry { Provider = "Ollama", Model = "phi3:mini", HealthCheckUrl = "http://localhost:11434/api/tags" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Equal(2,                  result.Count);
        Assert.Equal(LlmProvider.Groq,   result[0].Provider);
        Assert.Equal(LlmProvider.Ollama, result[1].Provider);
    }

    [Fact]
    public async Task GetViableFallbacksAsync_ReturnsFirstOnly_WhenSecondHealthCheckFails()
    {
        var factory = FactoryReturning(HttpStatusCode.ServiceUnavailable);
        var chain   = Build(enabled: true, factory
                          , new LlmFallbackEntry { Provider = "Groq",   Model = "llama-3.1-8b-instant" }
                          , new LlmFallbackEntry { Provider = "Ollama", Model = "phi3:mini", HealthCheckUrl = "http://localhost:11434/api/tags" });

        var result = await chain.GetViableFallbacksAsync();

        Assert.Single(result);
        Assert.Equal(LlmProvider.Groq, result[0].Provider);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LlmRouter — 429 fallback behaviour
// ─────────────────────────────────────────────────────────────────────────────

public class LlmRouterFallbackTests
{
    private readonly Mock<ILlmClientFactory>   _factoryMock       = new();
    private readonly Mock<ILlmFallbackChain>   _fallbackChainMock = new();
    private readonly Mock<ILlmRateLimiter>     _rateLimiterMock   = new();
    private readonly Mock<ILlmCapacityRouter>  _capacityMock      = new();
    private readonly Mock<IPromptLogger>       _loggerMock        = new();
    private readonly Mock<ILlmUsageAggregator> _aggregatorMock    = new();

    public LlmRouterFallbackTests()
    {
        _factoryMock.Setup(factory => factory.DefaultProvider).Returns(LlmProvider.Groq);
        _rateLimiterMock.Setup(limiter => limiter.IsExhausted(It.IsAny<string>())).Returns(false);
    }

    private LlmRouter BuildRouter() =>
        new(_factoryMock.Object
          , Options.Create(new LlmProviderDefaults { Groq = "llama-3.3-70b-versatile" })
          , _loggerMock.Object
          , _aggregatorMock.Object
          , _rateLimiterMock.Object
          , _capacityMock.Object
          , _fallbackChainMock.Object);

    private static ILlmClient ClientThrowing429()
    {
        var mock = new Mock<ILlmClient>();
        mock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Groq API returned 429: rate limit exceeded"));
        return mock.Object;
    }

    private static ILlmClient ClientReturning(string content)
    {
        var response = new LlmResponse { Content = content };
        var mock     = new Mock<ILlmClient>();
        mock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        return mock.Object;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_UsesFallbackModel_WhenPrimaryReturns429()
    {
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(true);
        _fallbackChainMock.Setup(chain => chain.FallbackNote).Returns("rate-limited");
        _fallbackChainMock
            .Setup(chain => chain.GetViableFallbacksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([(LlmProvider.Ollama, "phi3:mini")]);

        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq)).Returns(ClientThrowing429());
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Ollama)).Returns(ClientReturning("fallback ok"));

        var result = await BuildRouter().SendAsync("prompt", new ConversationContext("test"));

        Assert.Equal("fallback ok", result.Content);
    }

    [Fact]
    public async Task SendAsync_ReturnsErrorMessage_WhenAllFallbacksAlso429()
    {
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(true);
        _fallbackChainMock.Setup(chain => chain.FallbackNote).Returns("rate-limited");
        _fallbackChainMock
            .Setup(chain => chain.GetViableFallbacksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([(LlmProvider.Ollama, "phi3:mini")]);

        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq)).Returns(ClientThrowing429());
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Ollama)).Returns(ClientThrowing429());

        var result = await BuildRouter().SendAsync("prompt", new ConversationContext("test"));

        Assert.Contains("unavailable", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_UpdatesContextModelMetadata_AfterSuccessfulFallback()
    {
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(true);
        _fallbackChainMock.Setup(chain => chain.FallbackNote).Returns("rate-limited");
        _fallbackChainMock
            .Setup(chain => chain.GetViableFallbacksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([(LlmProvider.Ollama, "phi3:mini")]);

        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq)).Returns(ClientThrowing429());
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Ollama)).Returns(ClientReturning("ok"));

        var context = new ConversationContext("test");
        await BuildRouter().SendAsync("prompt", context);

        Assert.True(context.Metadata.TryGetValue("model", out var model));
        Assert.Equal("phi3:mini", model);
    }

    [Fact]
    public async Task SendAsync_UsesFallbacksInOrder_SkippingFirstWhenAlso429()
    {
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(true);
        _fallbackChainMock.Setup(chain => chain.FallbackNote).Returns("rate-limited");
        _fallbackChainMock
            .Setup(chain => chain.GetViableFallbacksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                    (LlmProvider.Gemini, "gemini-flash")  // will also 429
                  , (LlmProvider.Ollama, "phi3:mini")     // succeeds
            ]);

        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq)).Returns(ClientThrowing429());
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Gemini)).Returns(ClientThrowing429());
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Ollama)).Returns(ClientReturning("ollama ok"));

        var result = await BuildRouter().SendAsync("prompt", new ConversationContext("test"));

        Assert.Equal("ollama ok", result.Content);
    }

    [Fact]
    public async Task SendAsync_UsesFallbacksInOrder_ReturnsErrorWhenAllExhausted()
    {
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(true);
        _fallbackChainMock.Setup(chain => chain.FallbackNote).Returns("rate-limited");
        _fallbackChainMock
            .Setup(chain => chain.GetViableFallbacksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                    (LlmProvider.Gemini, "gemini-flash")
                  , (LlmProvider.Ollama, "phi3:mini")
            ]);

        _factoryMock.Setup(factory => factory.Create(It.IsAny<LlmProvider>())).Returns(ClientThrowing429());

        var result = await BuildRouter().SendAsync("prompt", new ConversationContext("test"));

        Assert.Contains("unavailable", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_DoesNotUseFallbackChain_WhenChainIsDisabled()
    {
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(false);

        var capacityModel = new LlmModelCapacity { ModelId = new LlmModelId("Gemini", "gemini-2.0-flash") };
        _capacityMock.Setup(router => router.SelectModel(It.IsAny<TaskComplexity>())).Returns(capacityModel);

        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq)).Returns(ClientThrowing429());
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Gemini)).Returns(ClientReturning("capacity ok"));

        var result = await BuildRouter().SendAsync("prompt", new ConversationContext("test"));

        Assert.Equal("capacity ok", result.Content);
        _fallbackChainMock.Verify(chain => chain.GetViableFallbacksAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

// ── Shared test helper ────────────────────────────────────────────────────────

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_respond(request));
}
