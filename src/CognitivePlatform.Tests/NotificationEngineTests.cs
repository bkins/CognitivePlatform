using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

public class NotificationEngineTests
{
    private readonly Mock<IInsightEngine>       _insightEngineMock = new();
    private readonly Mock<IInsightHistoryStore> _historyStoreMock  = new();
    private readonly NotificationEngine         _engine;

    public NotificationEngineTests()
    {
        _engine = new NotificationEngine(_insightEngineMock.Object, _historyStoreMock.Object);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsInsights_WhenProvidersFire()
    {
        var insight = new Insight
                      {
                              Message          = "You haven't journaled today."
                            , DeduplicationKey = "journal.no-entry-today"
                            , Category         = InsightCategory.Journal
                      };

        _insightEngineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { insight });

        _historyStoreMock
            .Setup(store => store.RecordEmittedAsync(It.IsAny<IReadOnlyList<Insight>>()
                                                   , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var results = await _engine.EvaluateAsync("session-1", DateTime.UtcNow);

        Assert.Single(results);
        Assert.Equal("journal.no-entry-today", results[0].DeduplicationKey);
    }

    [Fact]
    public async Task EvaluateAsync_CallsRecordEmittedAsync_WhenInsightsAreReturned()
    {
        var insight = new Insight
                      {
                              Message          = "You have 5 open tasks."
                            , DeduplicationKey = "tasks.open-tasks-reminder"
                            , Category         = InsightCategory.Tasks
                      };

        _insightEngineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { insight });

        _historyStoreMock
            .Setup(store => store.RecordEmittedAsync(It.IsAny<IReadOnlyList<Insight>>()
                                                   , It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _engine.EvaluateAsync("session-1", DateTime.UtcNow);

        _historyStoreMock.Verify(store => store.RecordEmittedAsync(
                                     It.Is<IReadOnlyList<Insight>>(list => list.Count == 1)
                                   , It.IsAny<CancellationToken>())
                               , Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsEmptyList_WhenNoInsightsFire()
    {
        _insightEngineMock
            .Setup(engine => engine.GenerateInsightsAsync(It.IsAny<ConversationContext>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Insight>());

        var results = await _engine.EvaluateAsync("session-1", DateTime.UtcNow);

        Assert.Empty(results);
        _historyStoreMock.Verify(store => store.RecordEmittedAsync(It.IsAny<IReadOnlyList<Insight>>()
                                                                 , It.IsAny<CancellationToken>())
                               , Times.Never);
    }
}
