using Moq;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Insights;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.SystemPromptLogging;

namespace CognitivePlatform.Tests;

public class InsightsActionsTests
{
    private readonly Mock<ITaskService>    _tasksMock        = new();
    private readonly Mock<IJournalService> _journalMock      = new();
    private readonly Mock<ILlmClient>      _llmMock          = new();
    private readonly Mock<IActivityLog>    _activityMock     = new();
    private readonly Mock<IPromptLogger>   _promptLoggerMock = new();
    private readonly InsightsActions       _actions;

    public InsightsActionsTests()
    {
        _activityMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                               , It.IsAny<DateTimeOffset?>()
                                               , It.IsAny<CancellationToken>()))
                     .ReturnsAsync((IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>());

        _actions = new InsightsActions(_tasksMock.Object
                                     , _journalMock.Object
                                     , _llmMock.Object
                                     , _promptLoggerMock.Object
                                     , _activityMock.Object);
    }

    // ================================================================
    // HAPPY PATH
    // ================================================================

    [Fact]
    public async Task AnalyzePatterns_ReturnsLlmResponse_WhenDataExists()
    {
        SetupWithTask("Finish report");

        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                          , It.IsAny<string?>()
                                          , It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse { Content = "You are very productive." });

        var result = await _actions.AnalyzePatterns();

        Assert.Equal("You are very productive.", result);
    }

    [Fact]
    public async Task AnalyzePatterns_IncludesTaskStatusLabels_InPrompt()
    {
        var completedTask = new TaskItem
                            {
                                    ShortDescription = "Done task"
                                  , CompletedAt      = DateTimeOffset.UtcNow
                            };
        var deletedTask = new TaskItem
                          {
                                  ShortDescription = "Deleted task"
                                , IsDeleted        = true
                          };
        var activeTask  = new TaskItem { ShortDescription = "Active task" };

        _tasksMock.Setup(svc => svc.List(It.IsAny<DateTimeOffset?>()
                                        , It.IsAny<DateTimeOffset?>()
                                        , true))
                  .Returns(new List<TaskItem> { completedTask, deletedTask, activeTask });
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.AnalyzePatterns();

        Assert.NotNull(capturedPrompt);
        Assert.Contains("[Done]",    capturedPrompt);
        Assert.Contains("[Deleted]", capturedPrompt);
        Assert.Contains("[Active]",  capturedPrompt);
    }

    [Fact]
    public async Task AnalyzePatterns_IncludesFocusLabel_InPrompt()
    {
        SetupWithTask("Review budget");

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.AnalyzePatterns(focus: "work-life balance");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("work-life balance", capturedPrompt);
    }

    // ================================================================
    // EMPTY DATA
    // ================================================================

    [Fact]
    public async Task AnalyzePatterns_ReturnsNoDataMessage_WhenBothEmpty()
    {
        _tasksMock.Setup(svc => svc.List(It.IsAny<DateTimeOffset?>()
                                        , It.IsAny<DateTimeOffset?>()
                                        , true))
                  .Returns(new List<TaskItem>());
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = await _actions.AnalyzePatterns();

        Assert.Contains("No tasks, journal, or activity entries found", result);
        _llmMock.Verify(llm => llm.SendAsync(It.IsAny<string>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<CancellationToken>())
                      , Times.Never);
    }

    // ================================================================
    // ACTIVITY CONTEXT
    // ================================================================

    [Fact]
    public async Task AnalyzePatterns_IncludesActivitySection_WhenEventsExist()
    {
        SetupWithTask("Write code");

        _activityMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                               , It.IsAny<DateTimeOffset?>()
                                               , It.IsAny<CancellationToken>()))
                     .ReturnsAsync((IReadOnlyList<ActivityEvent>)new List<ActivityEvent>
                                                                {
                                                                        new()
                                                                        {
                                                                                ActivityType = "run"
                                                                              , Duration     = 30
                                                                              , Unit         = "minutes"
                                                                              , OccurredUtc  = DateTimeOffset.UtcNow
                                                                        }
                                                                });

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                          , It.IsAny<string?>()
                                          , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.AnalyzePatterns();

        Assert.NotNull(capturedPrompt);
        Assert.Contains("=== Recent Activities ===", capturedPrompt);
        Assert.Contains("run",                        capturedPrompt);
        Assert.Contains("30 minutes",                 capturedPrompt);
    }

    [Fact]
    public async Task AnalyzePatterns_OmitsActivitySection_WhenNoEvents()
    {
        SetupWithTask("Write code");

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                          , It.IsAny<string?>()
                                          , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.AnalyzePatterns();

        Assert.NotNull(capturedPrompt);
        Assert.DoesNotContain("Recent Activities", capturedPrompt);
    }

    [Fact]
    public async Task AnalyzePatterns_OmitsActivitySection_WhenActivityLogIsNull()
    {
        _tasksMock.Setup(svc => svc.List(It.IsAny<DateTimeOffset?>()
                                        , It.IsAny<DateTimeOffset?>()
                                        , true))
                  .Returns(new List<TaskItem> { new() { ShortDescription = "Only task" } });
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var actionsNoActivity = new InsightsActions(_tasksMock.Object
                                                  , _journalMock.Object
                                                  , _llmMock.Object
                                                  , _promptLoggerMock.Object);

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                          , It.IsAny<string?>()
                                          , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await actionsNoActivity.AnalyzePatterns();

        Assert.NotNull(capturedPrompt);
        Assert.DoesNotContain("Recent Activities", capturedPrompt);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private void SetupWithTask(string description)
    {
        _tasksMock.Setup(svc => svc.List(It.IsAny<DateTimeOffset?>()
                                        , It.IsAny<DateTimeOffset?>()
                                        , true))
                  .Returns(new List<TaskItem> { new() { ShortDescription = description } });
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());
    }
}
