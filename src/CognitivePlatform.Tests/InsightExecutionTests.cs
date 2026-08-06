using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class InsightExecutionTests
{
    private readonly Mock<IInsightEngine> _insightEngineMock = new();
    private readonly InsightActions       _actions;

    public InsightExecutionTests()
    {
        _actions = new InsightActions(_insightEngineMock.Object);
    }

    [Fact]
    public async Task RunInsightsNow_WhenInsightsFound_ReturnsSuccessfulResultWithMessages()
    {
        var insights = new List<Insight>
                       {
                           new()
                           {
                               Message          = "Late dinner sleep correlation insight."
                             , DeduplicationKey = "health.meals.latedinner"
                             , Category         = InsightCategory.Health
                           }
                       };

        _insightEngineMock.Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(insights);

        var result = await _actions.RunInsightsNow();

        Assert.True(result.Success);
        Assert.Contains("Late dinner sleep correlation insight.", result.Message);
    }

    [Fact]
    public async Task RunInsightsNow_WhenNoInsightsFound_ReturnsSuccessfulResultWithDefaultMessage()
    {
        _insightEngineMock.Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new List<Insight>());

        var result = await _actions.RunInsightsNow();

        Assert.True(result.Success);
        Assert.Contains("No new actionable insights generated", result.Message);
    }

    [Fact]
    public async Task RunInsightPassAsync_ResolvesEngineAndExecutesGenerateInsights()
    {
        var insights = new List<Insight>
                       {
                           new() { Message = "Insight 1", DeduplicationKey = "k1", Category = InsightCategory.Health }
                         , new() { Message = "Insight 2", DeduplicationKey = "k2", Category = InsightCategory.Habit }
                       };

        _insightEngineMock.Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(insights);

        var scopeMock           = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeFactoryMock    = new Mock<IServiceScopeFactory>();
        var loggerMock          = new Mock<ILogger<OffPeakInsightService>>();

        serviceProviderMock.Setup(provider => provider.GetService(typeof(IInsightEngine)))
                           .Returns(_insightEngineMock.Object);
        scopeMock.Setup(scope => scope.ServiceProvider)
                 .Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(factory => factory.CreateScope())
                        .Returns(scopeMock.Object);

        var service = new OffPeakInsightService(scopeFactoryMock.Object, loggerMock.Object);

        var count = await service.RunInsightPassAsync();

        Assert.Equal(2, count);
        _insightEngineMock.Verify(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
