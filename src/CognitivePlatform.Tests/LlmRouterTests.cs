using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.SystemPromptLogging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class LlmRouterTests
{
    private readonly Mock<ILlmClientFactory> _factoryMock = new();
    private readonly Mock<ILlmClient>        _groqClient  = new();
    private readonly Mock<ILlmClient>        _openRouter  = new();
    private readonly Mock<ILlmClient>        _gemini      = new();
    private readonly Mock<IPromptLogger>     _loggerMock  = new();

    private readonly LlmProviderDefaults _defaults;
    private readonly LlmRouter           _router;

    public LlmRouterTests()
    {
        _defaults = new LlmProviderDefaults
                    {
                            Groq       = "llama-3.3-70b-versatile"
                          , OpenRouter = "anthropic/claude-3.5-sonnet"
                          , Gemini     = "gemini-2.5-flash"
                    };

        _factoryMock.SetupGet(factory => factory.DefaultProvider).Returns(LlmProvider.Groq);
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Groq))      .Returns(_groqClient.Object);
        _factoryMock.Setup(factory => factory.Create(LlmProvider.OpenRouter)).Returns(_openRouter.Object);
        _factoryMock.Setup(factory => factory.Create(LlmProvider.Gemini))    .Returns(_gemini.Object);

        _router = new LlmRouter(_factoryMock.Object
                              , Options.Create(_defaults)
                              , _loggerMock.Object);
    }

    [Fact]
    public async Task SendAsync_UsesDefaultProvider_WhenSessionHasNoProvider()
    {
        var context = new ConversationContext("session-1");
        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("groq-response");

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("groq-response", result);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Groq), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesSessionProvider_WhenSessionProviderIsSet()
    {
        var context = new ConversationContext("session-2");
        context.SetLlmSession("OpenRouter", string.Empty);

        _openRouter.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("openrouter-response");

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("openrouter-response", result);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.OpenRouter), Times.Once);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Groq),       Times.Never);
    }

    [Fact]
    public async Task SendAsync_IsCaseInsensitive_ForSessionProviderValue()
    {
        var context = new ConversationContext("session-3");
        context.SetLlmSession("gemini", string.Empty);

        _gemini.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("gemini-response");

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("gemini-response", result);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Gemini), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FallsBackToDefaultProvider_WhenSessionProviderIsUnknown()
    {
        var context = new ConversationContext("session-4");
        context.SetLlmSession("Imaginary", string.Empty);

        _groqClient.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("groq-response");

        var result = await _router.SendAsync("hello", context);

        Assert.Equal("groq-response", result);
        _factoryMock.Verify(factory => factory.Create(LlmProvider.Groq), Times.Once);
    }

    [Fact]
    public async Task SendAsync_PassesSessionModel_WhenPresent()
    {
        var context = new ConversationContext("session-5");
        context.SetLlmSession("OpenRouter", "custom-model");

        string? capturedModel = null;
        _openRouter.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .Callback<string, string?, CancellationToken>((_, m, _) => capturedModel = m)
                   .ReturnsAsync(string.Empty);

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
                   .Callback<string, string?, CancellationToken>((_, m, _) => capturedModel = m)
                   .ReturnsAsync(string.Empty);

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
                   .Callback<string, string?, CancellationToken>((_, m, _) => capturedModel = m)
                   .ReturnsAsync(string.Empty);

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
                   .ReturnsAsync(string.Empty);

        await _router.SendAsync("hello", context, cts.Token);

        Assert.Equal(cts.Token, captured);
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

    private static async IAsyncEnumerable<string> AsAsync(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
