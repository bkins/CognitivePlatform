using System;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Insights;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.SystemPromptLogging;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class InsightsActionsTests
{
    private readonly Mock<IPatternDataAggregator> _aggregatorMock   = new();
    private readonly Mock<ILlmClient>             _llmMock          = new();
    private readonly Mock<IPromptLogger>          _promptLoggerMock = new();
    private readonly Mock<IInsightEngine>         _engineMock       = new();
    private readonly InsightsActions              _actions;

    public InsightsActionsTests()
    {
        _actions = new InsightsActions( _aggregatorMock.Object
                                      , _llmMock.Object
                                      , _promptLoggerMock.Object
                                      , _engineMock.Object );
    }

    [Fact]
    public async Task AnalyzePatterns_ReturnsLlmResponse_WhenAggregatorReturnsPrompt()
    {
        _aggregatorMock.Setup(agg => agg.AggregateAndFormatAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync("Formatted Data Prompt");

        _llmMock.Setup(llm => llm.SendAsync("Formatted Data Prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse { Content = "You are very productive." });

        var result = await _actions.AnalyzePatterns();

        Assert.Equal("You are very productive.", result);
        _promptLoggerMock.Verify(logger => logger.Log("InsightsAnalysisPrompt", "Formatted Data Prompt", _llmMock.Object.GetType().Name), Times.Once);
    }

    [Fact]
    public async Task AnalyzePatterns_ReturnsNoDataMessage_WhenAggregatorReturnsNull()
    {
        _aggregatorMock.Setup(agg => agg.AggregateAndFormatAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((string?)null);

        var result = await _actions.AnalyzePatterns();

        Assert.Contains("No tasks, journal, or activity entries found", result);
        _llmMock.Verify(llm => llm.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnalyzePatterns_PassesParametersToAggregator()
    {
        _aggregatorMock.Setup(agg => agg.AggregateAndFormatAsync("sleep", "2026-08-01", "2026-08-05", It.IsAny<CancellationToken>()))
                       .ReturnsAsync("Prompt");

        _llmMock.Setup(llm => llm.SendAsync("Prompt", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse { Content = "Analysis result." });

        await _actions.AnalyzePatterns("sleep", "2026-08-01", "2026-08-05");

        _aggregatorMock.Verify(agg => agg.AggregateAndFormatAsync("sleep", "2026-08-01", "2026-08-05", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunInsightsNow_ReturnsErrorResult_WhenEngineIsNull()
    {
        var actionsNoEngine = new InsightsActions(_aggregatorMock.Object, _llmMock.Object, _promptLoggerMock.Object);

        var result = await actionsNoEngine.RunInsightsNow();

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Message);
    }
}
