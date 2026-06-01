using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class GoalAlignmentInsightProviderTests
{
    private readonly Mock<IIdentityService>              _identityMock = new();
    private readonly Mock<ITaskService>                  _taskMock     = new();
    private readonly Mock<ILlmRouter>                    _routerMock   = new();
    private readonly GoalAlignmentInsightProvider        _provider;

    public GoalAlignmentInsightProviderTests()
    {
        _provider = new GoalAlignmentInsightProvider(
            _identityMock.Object
          , _taskMock.Object
          , _routerMock.Object
          , NullLogger<GoalAlignmentInsightProvider>.Instance);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    private static PersonProfile EmptyProfile() => new() { LongTermGoals = [] };

    private static PersonProfile ProfileWithGoals(params string[] goals)
        => new() { LongTermGoals = goals };

    private void SetupLlmResponse(string json)
    {
        _routerMock
            .Setup(router => router.SendAsync(
                It.IsAny<string>()
              , It.IsAny<ConversationContext>()
              , It.IsAny<TaskComplexity>()
              , It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse { Content = json });
    }

    private void SetupEmptyTasks()
    {
        _taskMock.Setup(service => service.GetCompleted()).Returns([]);
        _taskMock.Setup(service => service.GetActive()).Returns([]);
    }

    // ====================================================================
    // Core logic — suppress when no goals
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenLongTermGoalsIsEmpty()
    {
        _identityMock
            .Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyProfile());

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
        _routerMock.Verify(router => router.SendAsync(
                It.IsAny<string>()
              , It.IsAny<ConversationContext>()
              , It.IsAny<TaskComplexity>()
              , It.IsAny<CancellationToken>())
            , Times.Never);
    }

    // ====================================================================
    // Core logic — gap insight fires
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsGapInsight_WhenLlmDetectsGap()
    {
        _identityMock
            .Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileWithGoals("run a marathon", "learn piano"));

        SetupEmptyTasks();
        SetupLlmResponse("""{"type":"gap","goal":"run a marathon","message":"You haven't had any running-related tasks this week — want to add one?"}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("goals.alignment.run-a-marathon", single.DeduplicationKey);
        Assert.Equal(InsightCategory.Wellbeing, single.Category);
        Assert.Equal(InsightPriority.Normal,    single.Priority);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("gap",                  single.Reasoning.Explanation);
    }

    // ====================================================================
    // Core logic — win insight fires
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsWinInsight_WhenLlmDetectsAlignment()
    {
        _identityMock
            .Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileWithGoals("improve fitness"));

        var now = DateTimeOffset.UtcNow;
        var completedTask = new TaskItem
        {
            ShortDescription = "went for a run"
          , CompletedAt      = now.AddDays(-1)
        };
        _taskMock.Setup(service => service.GetCompleted()).Returns([completedTask]);
        _taskMock.Setup(service => service.GetActive()).Returns([]);

        SetupLlmResponse("""{"type":"win","goal":"improve fitness","message":"3 of your completed tasks this week align with your fitness goal — great momentum!"}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.StartsWith("goals.alignment.", single.DeduplicationKey);
        Assert.Contains("win",               single.Reasoning!.Explanation);
    }

    // ====================================================================
    // Core logic — suppress when LLM returns "none"
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenLlmReturnsNone()
    {
        _identityMock
            .Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileWithGoals("learn piano"));

        SetupEmptyTasks();
        SetupLlmResponse("""{"type":"none","goal":"","message":""}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Max 1 insight per turn
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsAtMostOneInsight_WhenMultipleGoalsExist()
    {
        _identityMock
            .Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileWithGoals("run a marathon", "learn piano", "write a novel"));

        SetupEmptyTasks();
        // LLM returns a single result (the provider yields only once)
        SetupLlmResponse("""{"type":"gap","goal":"run a marathon","message":"No running tasks this week."}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        // Provider must yield at most 1, regardless of how many goals exist
        Assert.True(insights.Count <= 1);
    }

    // ====================================================================
    // Category property
    // ====================================================================

    [Fact]
    public void Category_IsWellbeing()
    {
        Assert.Equal(InsightCategory.Wellbeing, _provider.Category);
    }
}
