using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class ConversationReflectionInsightProviderTests
{
    private readonly Mock<ILlmRouter> _routerMock = new();
    private readonly InsightPolicy    _policy     = new();

    private ConversationReflectionInsightProvider BuildProvider() =>
        new(_routerMock.Object
          , _policy
          , NullLogger<ConversationReflectionInsightProvider>.Instance);

    private static ConversationContext MakeContext(string? lastMessage) =>
        new("session-r") { LastUserMessage = lastMessage };

    private void StubRouterReply(string reply) =>
        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                  , It.IsAny<ConversationContext>()
                                                  , It.IsAny<CancellationToken>()))
                   .ReturnsAsync(reply);

    [Fact]
    public void Category_Is_Reflection()
    {
        Assert.Equal(InsightCategory.Reflection, BuildProvider().Category);
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_AndDoesNotCallRouter_WhenLastUserMessageIsBlank()
    {
        var provider = BuildProvider();

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(MakeContext(null)))
            insights.Add(insight);

        Assert.Empty(insights);
        _routerMock.Verify(router => router.SendAsync(It.IsAny<string>()
                                                   , It.IsAny<ConversationContext>()
                                                   , It.IsAny<CancellationToken>())
                         , Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_PassesUserMessageIntoPrompt_WhenSetting()
    {
        const string userMessage = "I am completely overwhelmed with everything.";
        StubRouterReply("""{ "insights": [] }""");

        var provider = BuildProvider();

        await foreach (var _ in provider.GenerateAsync(MakeContext(userMessage)))
        {
            // drain
        }

        _routerMock.Verify(router => router.SendAsync(
                                It.Is<string>(prompt => prompt.Contains(userMessage))
                              , It.IsAny<ConversationContext>()
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenRouterReturnsOneStructuredEntry()
    {
        const string canned = """
            {
              "insights": [
                {
                  "message":          "It sounds like a lot is going on. Want to log how you're feeling?",
                  "suggestedAction":  "AddJournalEntry",
                  "deduplicationKey": "reflection.stress-detected"
                }
              ]
            }
            """;
        StubRouterReply(canned);

        var provider = BuildProvider();

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(MakeContext("I'm so overwhelmed.")))
            insights.Add(insight);

        var single = Assert.Single(insights);

        Assert.Equal(InsightCategory.Reflection, single.Category);
        Assert.Equal("AddJournalEntry",          single.SuggestedAction);
        Assert.Contains("session-r",             single.DeduplicationKey);
        Assert.StartsWith("reflection.stress-detected.", single.DeduplicationKey);
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenRouterReturnsEmptyInsights()
    {
        StubRouterReply("""{ "insights": [] }""");

        var provider = BuildProvider();

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(MakeContext("anything goes")))
            insights.Add(insight);

        Assert.Empty(insights);
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenRouterReturnsMalformedJson()
    {
        StubRouterReply("not even json, just prose from a confused model.");

        var provider = BuildProvider();

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(MakeContext("anything goes")))
            insights.Add(insight);

        Assert.Empty(insights);
    }

    [Fact]
    public async Task GenerateAsync_TolerantOfModelWrappingJsonInProseOrFences()
    {
        const string fenced = """
            Here is the JSON you asked for:
            ```json
            { "insights": [
                { "message": "Want to journal about that?",
                  "suggestedAction": null,
                  "deduplicationKey": "reflection.unspecified" } ] }
            ```
            Hope that helps!
            """;
        StubRouterReply(fenced);

        var provider = BuildProvider();

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(MakeContext("hmm.")))
            insights.Add(insight);

        var single = Assert.Single(insights);
        Assert.Null(single.SuggestedAction);
        Assert.Equal("Want to journal about that?", single.Message);
    }

    [Fact]
    public async Task GenerateAsync_SkipsInsight_WhenMessageIsBlank()
    {
        const string blankMessage = """
            { "insights": [
                { "message": "   ",
                  "suggestedAction": null,
                  "deduplicationKey": "reflection.skip-me" } ] }
            """;
        StubRouterReply(blankMessage);

        var provider = BuildProvider();

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(MakeContext("hmm.")))
            insights.Add(insight);

        Assert.Empty(insights);
    }
}
