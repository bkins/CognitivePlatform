using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.SystemPromptLogging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class LlmRouterTests
{
    private readonly Mock<ILlmClientFactory>   _factoryMock        = new();
    private readonly Mock<ILlmClient>          _groqClient         = new();
    private readonly Mock<ILlmClient>          _openRouter         = new();
    private readonly Mock<ILlmClient>          _gemini             = new();
    private readonly Mock<IPromptLogger>       _loggerMock         = new();
    private readonly Mock<ILlmUsageAggregator> _aggregatorMock     = new();
    private readonly Mock<ILlmRateLimiter>     _rateLimiterMock    = new();
    private readonly Mock<ILlmCapacityRouter>  _capacityRouterMock = new();
    private readonly Mock<ILlmFallbackChain>   _fallbackChainMock  = new();

    private readonly LlmProviderDefaults _defaults;
    private readonly LlmRouter           _router;

    // Helper so tests stay concise
    private static LlmResponse ForContent(string content) => new() { Content = content };

    public LlmRouterTests()
    {
        // LlmProviderDefaults was refactored from a plain POCO to a read-through over LlmClientSettings.
        // Build it the same way the DI container does.
        var clientSettings = new LlmClientSettings
                             {
                                 Groq       = new GroqSettings       { Model = "llama-3.3-70b-versatile"      }
                               , OpenRouter = new OpenRouterSettings { Model = "anthropic/claude-3.5-sonnet" }
                               , Gemini     = new GeminiSettings     { Model = "gemini-2.5-flash"             }
                             };

        _defaults = new LlmProviderDefaults(Options.Create(clientSettings));

        _factoryMock.SetupGet(factory => factory.DefaultProvider).Returns(LlmProvider.Groq);
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq))      .Returns(_groqClient.Object);
        _factoryMock.Setup(factory => factory.Create(LlmProvider.OpenRouter)).Returns(_openRouter.Object);
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Gemini))    .Returns(_gemini.Object);

        // Default: no provider is exhausted — session routing proceeds normally.
        _rateLimiterMock.Setup(limiter => limiter.IsExhausted(It.IsAny<string>())).Returns(false);

        // Fallback chain disabled by default so existing tests are unaffected.
        _fallbackChainMock.Setup(chain => chain.Enabled).Returns(false);

        _router = new LlmRouter(_factoryMock.Object
                              , _defaults
                              , _loggerMock.Object
                              , _aggregatorMock.Object
                              , _rateLimiterMock.Object
                              , _capacityRouterMock.Object
                              , _fallbackChainMock.Object);
    }

    [Fact]
    public async Task SendAsync_UsesDefaultProvider_WhenSessionHasNoProvider()
    {
        var context = new ConversationContext("session-1");
        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ForContent("groq-response"));

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("groq-response", result.Content);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Groq), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesSessionProvider_WhenSessionProviderIsSet()
    {
        var context = new ConversationContext("session-2");
        context.SetLlmSession("OpenRouter", string.Empty);

        _openRouter.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ForContent("openrouter-response"));

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("openrouter-response", result.Content);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.OpenRouter), Times.Once);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Groq),       Times.Never);
    }

    [Fact]
    public async Task SendAsync_IsCaseInsensitive_ForSessionProviderValue()
    {
        var context = new ConversationContext("session-3");
        context.SetLlmSession("gemini", string.Empty);

        _gemini.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(ForContent("gemini-response"));

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("gemini-response", result.Content);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Gemini), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FallsBackToDefaultProvider_WhenSessionProviderIsUnknown()
    {
        var context = new ConversationContext("session-4");
        context.SetLlmSession("Imaginary", string.Empty);

        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ForContent("groq-response"));

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("groq-response", result.Content);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Groq), Times.Once);
    }

    [Fact]
    public async Task SendAsync_PassesSessionModel_WhenPresent()
    {
        var context = new ConversationContext("session-5");
        context.SetLlmSession("OpenRouter", "custom-model");

        string? capturedModel = null;
        _openRouter.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string?, CancellationToken>((_, model, _) => capturedModel = model)
                   .ReturnsAsync(new LlmResponse());

        await _router.SendAsync("hello", context);

        Assert.Equal("custom-model", capturedModel);
    }

    [Fact]
    public async Task SendAsync_PassesProviderDefaultModel_WhenNoSessionModel()
    {
        var context = new ConversationContext("session-6");
        context.SetLlmSession("OpenRouter", string.Empty);

        string? capturedModel = null;
        _openRouter.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string?, CancellationToken>((_, model, _) => capturedModel = model)
                   .ReturnsAsync(new LlmResponse());

        await _router.SendAsync("hello", context);

        Assert.Equal("anthropic/claude-3.5-sonnet", capturedModel);
    }

    [Fact]
    public async Task SendAsync_PrefersPerTurnModelKey_OverSessionModel()
    {
        var context = new ConversationContext("session-7");
        context.SetLlmModel("session-model");
        context.Metadata["model"] = "per-turn-model";

        string? capturedModel = null;
        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string?, CancellationToken>((_, model, _) => capturedModel = model)
                   .ReturnsAsync(new LlmResponse());

        await _router.SendAsync("hello", context);

        Assert.Equal("per-turn-model", capturedModel);
    }

    [Fact]
    public async Task SendAsync_ThreadsCancellationToken_ThroughToClient()
    {
        var context = new ConversationContext("session-8");
        using var cts = new CancellationTokenSource();

        CancellationToken captured = default;
        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string?, CancellationToken>((_, _, token) => captured = token)
                   .ReturnsAsync(new LlmResponse());

        await _router.SendAsync("hello", context, ct: cts.Token);

        Assert.Equal(cts.Token, captured);
    }

    [Fact]
    public async Task SendAsync_RecordsUsage_AfterSuccessfulCall()
    {
        var context  = new ConversationContext("session-usage");
        var usage    = new LlmUsageInfo { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 };
        var response = new LlmResponse { Content = "hi", Usage = usage };

        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(response);

        await _router.SendAsync("hello", context);

        _aggregatorMock.Verify(agg => agg.Record(It.Is<LlmResponseMetadata>(m => m.Usage == usage)), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UpdatesRateLimiter_AfterSuccessfulCall()
    {
        var context   = new ConversationContext("session-ratelimit");
        var snapshot  = new LlmRateLimitSnapshot { Provider = "Groq", RequestLimit = 100, RequestsRemaining = 95 };
        var response  = new LlmResponse { Content = "hi", RateLimits = snapshot };

        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(response);

        await _router.SendAsync("hello", context);

        _rateLimiterMock.Verify(limiter => limiter.Update(It.Is<LlmResponseMetadata>(m => m.RateLimits == snapshot)), Times.Once);
    }

    [Fact]
    public async Task StreamAsync_DispatchesToSessionProvider()
    {
        var context = new ConversationContext("session-9");
        context.SetLlmSession("Gemini", string.Empty);

        _gemini.Setup(client => client.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .Returns(AsAsync("g1", "g2"));

        var chunks = new List<string>();
        await foreach (var chunk in _router.StreamAsync("hello", context))
            chunks.Add(chunk);

        Assert.Equal(new[] { "g1", "g2" }, chunks);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Gemini), Times.Once);
    }

    [Fact]
    public async Task StreamAsync_UsesFreshClient_AfterSessionProviderSwitch()
    {
        var context = new ConversationContext("session-10");

        _groqClient.Setup(client => client.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Returns(AsAsync("groq-chunk"));

        _openRouter.Setup(client => client.StreamAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Returns(AsAsync("or-chunk"));

        var first = new List<string>();
        await foreach (var chunk in _router.StreamAsync("first", context))
            first.Add(chunk);

        context.SetLlmSession("OpenRouter", "anthropic/claude-3.5-sonnet");

        var second = new List<string>();
        await foreach (var chunk in _router.StreamAsync("second", context))
            second.Add(chunk);

        Assert.Equal(new[] { "groq-chunk" }, first);
        Assert.Equal(new[] { "or-chunk" },   second);
    }

    // ----------------------------------------------------------------
    // ENH-19: complexity routing
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_DefaultComplexity_IsStandard()
    {
        var context = new ConversationContext("session-complexity-default");

        // Force the capacity router path so we can verify what complexity is passed.
        _rateLimiterMock.Setup(limiter => limiter.IsExhausted(It.IsAny<string>())).Returns(true);

        _capacityRouterMock
            .Setup(router => router.SelectModel(It.IsAny<TaskComplexity>()))
            .Returns(new LlmModelCapacity { ModelId = new LlmModelId("groq", "llama-3.3-70b-versatile") });

        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ForContent("response"));

        await _router.SendAsync("hello", context);

        _capacityRouterMock.Verify(router => router.SelectModel(TaskComplexity.Standard), Times.Once);
    }

    [Fact]
    public async Task WeaveAsync_PassesLightComplexity_ToInternalSendAsync()
    {
        var context  = new ConversationContext("session-weave-light");
        var insights = new List<CognitivePlatform.Api.Insights.Models.Insight>
                       {
                           new() { Message = "You tend to work late.", DeduplicationKey = "test" }
                       };

        // Force the capacity router path so complexity can be observed.
        _rateLimiterMock.Setup(limiter => limiter.IsExhausted(It.IsAny<string>())).Returns(true);

        _capacityRouterMock
            .Setup(router => router.SelectModel(It.IsAny<TaskComplexity>()))
            .Returns(new LlmModelCapacity { ModelId = new LlmModelId("groq", "llama-3.3-70b-versatile") });

        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ForContent("woven response"));

        await _router.WeaveAsync(context, "original response", insights);

        _capacityRouterMock.Verify(router => router.SelectModel(TaskComplexity.Light), Times.Once);
    }

    // ----------------------------------------------------------------
    // BUG-39: 429 retry / fallback
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_RetriesOnFallback_WhenPrimaryProviderReturns429()
    {
        var context = new ConversationContext("session-429-retry");

        _groqClient
            .Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Groq API returned 429: rate limit exceeded"));

        _capacityRouterMock
            .Setup(router => router.SelectModel(It.IsAny<TaskComplexity>()))
            .Returns(new LlmModelCapacity { ModelId = new LlmModelId("gemini", "gemini-2.0-flash") });

        _gemini
            .Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ForContent("gemini-fallback-response"));

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("gemini-fallback-response", result.Content);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Gemini), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ThrowsCapacityExceeded_WhenAllProviders429ed()
    {
        var context = new ConversationContext("session-all-exhausted");

        _groqClient
            .Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Groq API returned 429: rate limit exceeded"));

        _capacityRouterMock
            .Setup(router => router.SelectModel(It.IsAny<TaskComplexity>()))
            .Throws(new LlmCapacityExceededException("All providers exhausted."));

        await Assert.ThrowsAsync<LlmCapacityExceededException>(
            () => _router.SendAsync("hello", context));
    }

    private static async IAsyncEnumerable<string> AsAsync(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}

