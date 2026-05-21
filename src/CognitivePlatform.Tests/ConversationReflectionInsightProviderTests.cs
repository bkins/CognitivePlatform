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
    private          InsightPolicy    _policy     = new();

    private ConversationReflectionInsightProvider BuildProvider() =>
        new(_routerMock.Object
          , _policy
          , NullLogger<ConversationReflectionInsightProvider>.Instance);

    private static ConversationContext MakeContext(string? lastMessage) =>
        new("session-r") { LastUserMessage = lastMessage };

    private static ConversationContext MakeContextWithTurns(params (string user, string assistant)[] turns)
    {
        var context = new ConversationContext("session-r");
        var anchor  = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < turns.Length; i++)
        {
            context.RecordTurn(new ConversationTurn(
                                       UserMessage:      turns[i].user
                                     , AssistantMessage: turns[i].assistant
                                     , OccurredAt:       anchor.AddMinutes(i)
                                     , Path:             TurnPath.Interpreter));
        }
        // Mirror what the orchestrator does — keep LastUserMessage in sync with the
        // most recent recorded turn so fallback paths still have something to read.
        context.LastUserMessage = turns.Length > 0 ? turns[^1].user : null;
        return context;
    }

    private string? _capturedPrompt;

    private void StubRouterReply(string reply) =>
        _routerMock.Setup(router => router.SendAsync(It.IsAny<string>()
                                                  , It.IsAny<ConversationContext>()
                                                  , It.IsAny<TaskComplexity>()
                                                  , It.IsAny<CancellationToken>()))
                   .Callback<string, ConversationContext, TaskComplexity, CancellationToken>((prompt, _, _, _) => _capturedPrompt = prompt)
                   .ReturnsAsync(new LlmResponse { Content = reply });

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
                                                   , It.IsAny<TaskComplexity>()
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
                              , It.IsAny<TaskComplexity>()
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

    // ================================================================
    // ENH-08: multi-turn analysis window
    // ================================================================

    [Fact]
    public async Task GenerateAsync_UsesMultiTurnTemplate_WhenTurnsPopulated()
    {
        StubRouterReply("""{ "insights": [] }""");
        var context = MakeContextWithTurns(("first user", "first assistant")
                                         , ("second user", "second assistant"));

        var provider = BuildProvider();
        await foreach (var _ in provider.GenerateAsync(context)) { /* drain */ }

        Assert.NotNull(_capturedPrompt);
        Assert.Contains("Recent conversation",   _capturedPrompt);
        Assert.Contains("first user",            _capturedPrompt);
        Assert.Contains("first assistant",       _capturedPrompt);
        Assert.Contains("second user",           _capturedPrompt);
        Assert.Contains("second assistant",      _capturedPrompt);
    }

    [Fact]
    public async Task GenerateAsync_FallsBackToSingleTurnTemplate_WhenTurnsEmpty()
    {
        StubRouterReply("""{ "insights": [] }""");

        var provider = BuildProvider();
        await foreach (var _ in provider.GenerateAsync(MakeContext("just one shot"))) { /* drain */ }

        Assert.NotNull(_capturedPrompt);
        Assert.Contains("most recent message", _capturedPrompt);
        Assert.Contains("just one shot",       _capturedPrompt);
        Assert.DoesNotContain("Recent conversation", _capturedPrompt);
    }

    [Fact]
    public async Task GenerateAsync_HonorsMaxAnalysisTurns_TakingNewestFirst()
    {
        _policy = new InsightPolicy { MaxAnalysisTurns = 2, MaxAnalysisTokens = 10_000 };
        StubRouterReply("""{ "insights": [] }""");

        var context = MakeContextWithTurns(("oldest user",  "oldest assistant")
                                         , ("middle user",  "middle assistant")
                                         , ("newest user",  "newest assistant"));

        var provider = BuildProvider();
        await foreach (var _ in provider.GenerateAsync(context)) { /* drain */ }

        Assert.NotNull(_capturedPrompt);
        Assert.Contains("middle user",  _capturedPrompt);
        Assert.Contains("newest user",  _capturedPrompt);
        Assert.DoesNotContain("oldest user", _capturedPrompt);
    }

    [Fact]
    public async Task GenerateAsync_HonorsMaxAnalysisTokens_StoppingBudgetEarly()
    {
        // Budget of 50 tokens × 4 chars = 200 char budget.
        _policy = new InsightPolicy { MaxAnalysisTurns = 100, MaxAnalysisTokens = 50 };
        StubRouterReply("""{ "insights": [] }""");

        var bigUser      = new string('A', 150);
        var bigAssistant = new string('B', 150);
        var context      = MakeContextWithTurns(("first old turn", "first old reply")
                                              , (bigUser,           bigAssistant));

        var provider = BuildProvider();
        await foreach (var _ in provider.GenerateAsync(context)) { /* drain */ }

        Assert.NotNull(_capturedPrompt);
        Assert.Contains(bigUser,            _capturedPrompt);
        // Newest turn included (always); the older one would push us over budget so it's excluded.
        Assert.DoesNotContain("first old turn", _capturedPrompt);
    }

    [Fact]
    public async Task GenerateAsync_AlwaysIncludesNewestTurn_EvenWhenItExceedsBudgetAlone()
    {
        _policy = new InsightPolicy { MaxAnalysisTurns = 100, MaxAnalysisTokens = 1 };
        StubRouterReply("""{ "insights": [] }""");

        var context = MakeContextWithTurns(("hello", "world"));

        var provider = BuildProvider();
        await foreach (var _ in provider.GenerateAsync(context)) { /* drain */ }

        Assert.NotNull(_capturedPrompt);
        Assert.Contains("hello", _capturedPrompt);
        Assert.Contains("world", _capturedPrompt);
    }

    [Fact]
    public async Task GenerateAsync_ParsesJsonResponse_FromMultiTurnPath()
    {
        StubRouterReply("""
            { "insights": [
                { "message": "Want to journal about that?",
                  "suggestedAction": "AddJournalEntry",
                  "deduplicationKey": "reflection.stress-detected" } ] }
            """);

        var context = MakeContextWithTurns(("hi",                     "hello there")
                                         , ("I'm completely fried.",  "Sorry to hear that."));

        var provider = BuildProvider();
        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(context))
            insights.Add(insight);

        var single = Assert.Single(insights);
        Assert.Equal("AddJournalEntry", single.SuggestedAction);
        Assert.StartsWith("reflection.stress-detected.", single.DeduplicationKey);
    }
}
