using Moq;
using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

[Collection("LlmSharedState")]
public class WhyInsightTests
{
    private readonly Mock<IInsightHistoryStore> _historyStoreMock = new();

    private static ConversationContext MakeContext() => new("test-session");

    [Fact]
    public async Task WhyInsight_ReturnsReasoning_WhenRecentInsightWithReasoningExists()
    {
        var context = MakeContext();
        context.SetLastEmittedInsights(new[]
        {
            new EmittedInsightRef("tasks.open-tasks-reminder", "ListTasksSummary")
        });

        var reasoning = new InsightReasoning
                        {
                                Explanation = "You have 5 open tasks."
                              , Evidence    =
                                [
                                    new EvidenceReference("Task", "task-1")
                                  , new EvidenceReference("Task", "task-2")
                                ]
                        };

        var historyItem = new InsightHistoryItem
                          {
                                  InsightKey   = "tasks.open-tasks-reminder"
                                , Message      = "You have 5 open tasks. Want help prioritising?"
                                , EmittedAtUtc = DateTime.UtcNow
                                , Reasoning    = reasoning
                          };

        _historyStoreMock
            .Setup(store => store.GetRecentAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { historyItem });

        MetaActions.SetContext(context);
        MetaActions.SetInsightHistoryStore(_historyStoreMock.Object);

        var result = await MetaActions.WhyInsight();

        Assert.Contains("You have 5 open tasks.", result);
        Assert.Contains("Why:", result);
        Assert.Contains("Task: task-1", result);
        Assert.Contains("Task: task-2", result);
    }

    [Fact]
    public async Task WhyInsight_ReturnsGracefulMessage_WhenNoRecentInsightAvailable()
    {
        var context = MakeContext();
        context.SetLastEmittedInsights(Array.Empty<EmittedInsightRef>());

        _historyStoreMock
            .Setup(store => store.GetRecentAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InsightHistoryItem>());

        MetaActions.SetContext(context);
        MetaActions.SetInsightHistoryStore(_historyStoreMock.Object);

        var result = await MetaActions.WhyInsight();

        Assert.Contains("haven't made any suggestions recently", result);
    }
}
