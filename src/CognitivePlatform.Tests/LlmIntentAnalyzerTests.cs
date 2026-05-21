using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class LlmIntentAnalyzerTests
{
    private readonly Mock<ILlmRouter>    _routerMock = new();
    private readonly LlmIntentAnalyzer   _analyzer;

    public LlmIntentAnalyzerTests()
    {
        _analyzer = new LlmIntentAnalyzer(_routerMock.Object);
    }

    // ================================================================
    // Graceful failure — LLM throws → Unknown at 0.0
    // ================================================================

    [Fact]
    public async Task AnalyzeAsync_ReturnsUnknown_WhenLlmRouterThrows()
    {
        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                   , It.IsAny<ConversationContext>()
                                                   , It.IsAny<TaskComplexity>()
                                                   , It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        var result = await _analyzer.AnalyzeAsync("I have a bug");

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Equal(0.0,            result.Confidence);
    }

    // ================================================================
    // Happy path — LLM returns recognisable label
    // ================================================================

    [Theory]
    [InlineData("TechnicalHelp", Intent.TechnicalHelp)]
    [InlineData("Leadership",    Intent.Leadership)]
    [InlineData("Motivation",    Intent.Motivation)]
    [InlineData("GeneralHelp",   Intent.GeneralHelp)]
    public async Task AnalyzeAsync_ParsesIntent_WhenLlmReturnsValidLabel(string llmLabel, Intent expectedIntent)
    {
        ArrangeLlmResponse(llmLabel);

        var result = await _analyzer.AnalyzeAsync("some message");

        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(0.9,            result.Confidence);
    }

    // ================================================================
    // LLM returns unrecognisable label → Unknown at 0.0
    // ================================================================

    [Fact]
    public async Task AnalyzeAsync_ReturnsUnknown_WhenLlmReturnsUnparsableLabel()
    {
        ArrangeLlmResponse("CompletelyWrongLabel");

        var result = await _analyzer.AnalyzeAsync("some message");

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Equal(0.0,            result.Confidence);
    }

    // ================================================================
    // Case-insensitive label parsing
    // ================================================================

    [Fact]
    public async Task AnalyzeAsync_ParsesIntentCaseInsensitively()
    {
        ArrangeLlmResponse("technicalhelp");

        var result = await _analyzer.AnalyzeAsync("fix my code");

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
    }

    // ================================================================
    // Null / empty input — Unknown without hitting the router
    // ================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task AnalyzeAsync_ReturnsUnknown_WhenInputIsNullOrWhiteSpace(string? message)
    {
        var result = await _analyzer.AnalyzeAsync(message!);

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Equal(0.0,            result.Confidence);

        _routerMock.Verify(router => router.SendAsync(It.IsAny<string>()
                                                    , It.IsAny<ConversationContext>()
                                                    , It.IsAny<TaskComplexity>()
                                                    , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    // ================================================================
    // Multi-line LLM response — only first line is used
    // ================================================================

    [Fact]
    public async Task AnalyzeAsync_UsesFirstLine_WhenLlmReturnsMultipleLines()
    {
        ArrangeLlmResponse("TechnicalHelp\nSome extra explanation line");

        var result = await _analyzer.AnalyzeAsync("my code is broken");

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
    }

    // --- Helpers -----------------------------------------------------------------

    private void ArrangeLlmResponse(string content)
    {
        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                   , It.IsAny<ConversationContext>()
                                                   , It.IsAny<TaskComplexity>()
                                                   , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new LlmResponse { Content = content });
    }
}
