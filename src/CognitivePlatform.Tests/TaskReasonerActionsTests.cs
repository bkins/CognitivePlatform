using Moq;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Calendar;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.SystemPromptLogging;

namespace CognitivePlatform.Tests;

public class TaskReasonerActionsTests
{
    private readonly Mock<ITaskService>      _tasksMock     = new();
    private readonly Mock<IJournalService>   _journalMock   = new();
    private readonly Mock<ILlmClient>        _llmMock       = new();
    private readonly Mock<ICalendarProvider> _calendarMock  = new();
    private readonly Mock<IPromptLogger>     _promptLogMock = new();

    private readonly TaskReasonerActions _actions;

    public TaskReasonerActionsTests()
    {
        // Calendar not connected by default — existing tests are unaffected
        _calendarMock.SetupGet(cal => cal.IsConnected).Returns(false);

        _actions = new TaskReasonerActions(_tasksMock.Object
                                         , _journalMock.Object
                                         , _llmMock.Object
                                         , _promptLogMock.Object
                                         , _calendarMock.Object);
    }

    // ================================================================
    // HAPPY PATH
    // ================================================================

    [Fact]
    public async Task ReasonAboutTasks_ReturnsLlmResponse_WhenTasksExist()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem> { MakeTask("Finish report") });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                        , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                          , It.IsAny<string?>()
                                          , It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse { Content = "Focus on Finish report." });

        var result = await _actions.ReasonAboutTasks("What should I do next?");

        Assert.Equal("Focus on Finish report.", result);
    }

    [Fact]
    public async Task ReasonAboutTasks_IncludesTaskInPrompt()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem> { MakeTask("Finish report", isImportant: true, isUrgent: true) });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.ReasonAboutTasks("What should I do next?");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("Finish report",       capturedPrompt);
        Assert.Contains("[Important]",          capturedPrompt);
        Assert.Contains("[Urgent]",             capturedPrompt);
        Assert.Contains("What should I do next?", capturedPrompt);
    }

    [Fact]
    public async Task ReasonAboutTasks_IncludesJournalContext_WhenEntriesExist()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem>());
        _journalMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision> { MakeEntry("Felt tired today.") });

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.ReasonAboutTasks("How am I doing?");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("Felt tired today.", capturedPrompt);
    }

    // ================================================================
    // CALENDAR CONTEXT
    // ================================================================

    [Fact]
    public async Task ReasonAboutTasks_OmitsCalendarSection_WhenProviderIsNull()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem> { MakeTask("Write tests") });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                         , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        var actionsNoCalendar = new TaskReasonerActions(_tasksMock.Object
                                                      , _journalMock.Object
                                                      , _llmMock.Object
                                                      , _promptLogMock.Object);

        await actionsNoCalendar.ReasonAboutTasks("What should I do?");

        Assert.NotNull(capturedPrompt);
        Assert.DoesNotContain("Calendar", capturedPrompt, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================
    // EMPTY DATA
    // ================================================================

    [Fact]
    public async Task ReasonAboutTasks_ReturnsNoDataMessage_WhenBothEmpty()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem>());
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                 , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = await _actions.ReasonAboutTasks("What should I do?");

        Assert.Contains("no active tasks", result);
        _llmMock.Verify(llm => llm.SendAsync(It.IsAny<string>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<CancellationToken>())
                      , Times.Never);
    }

    // ================================================================
    // ACTIVITY CONTEXT
    // ================================================================

    [Fact]
    public async Task ReasonAboutTasks_IncludesActivitySection_WhenEventsExist()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem> { MakeTask("Write tests") });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                         , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var activityMock = new Mock<IActivityLog>();
        activityMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                              , It.IsAny<DateTimeOffset?>()
                                              , It.IsAny<CancellationToken>()))
                    .ReturnsAsync((IReadOnlyList<ActivityEvent>)new List<ActivityEvent>
                                                                {
                                                                        new() { ActivityType = "run", Duration = 30, Unit = "minutes", OccurredUtc = DateTimeOffset.UtcNow }
                                                                });

        var actions = new TaskReasonerActions(_tasksMock.Object
                                            , _journalMock.Object
                                            , _llmMock.Object
                                            , _promptLogMock.Object
                                            , _calendarMock.Object
                                            , activityMock.Object);

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await actions.ReasonAboutTasks("What should I do?");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("=== Recent Activities ===", capturedPrompt);
        Assert.Contains("run",                        capturedPrompt);
        Assert.Contains("30 minutes",                 capturedPrompt);
    }

    [Fact]
    public async Task ReasonAboutTasks_OmitsActivitySection_WhenActivityLogIsNull()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem> { MakeTask("Write tests") });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                         , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await _actions.ReasonAboutTasks("What should I do?");

        Assert.NotNull(capturedPrompt);
        Assert.DoesNotContain("Recent Activities", capturedPrompt);
    }

    [Fact]
    public async Task ReasonAboutTasks_OmitsActivitySection_WhenLogIsEmpty()
    {
        _tasksMock.Setup(service => service.GetActive())
                  .Returns(new List<TaskItem> { MakeTask("Write tests") });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                         , It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var activityMock = new Mock<IActivityLog>();
        activityMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>()
                                              , It.IsAny<DateTimeOffset?>()
                                              , It.IsAny<CancellationToken>()))
                    .ReturnsAsync((IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>());

        var actions = new TaskReasonerActions(_tasksMock.Object
                                            , _journalMock.Object
                                            , _llmMock.Object
                                            , _promptLogMock.Object
                                            , _calendarMock.Object
                                            , activityMock.Object);

        string? capturedPrompt = null;
        _llmMock.Setup(llm => llm.SendAsync(It.IsAny<string>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<CancellationToken>()))
                .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                .ReturnsAsync(new LlmResponse { Content = "Response." });

        await actions.ReasonAboutTasks("What should I do?");

        Assert.NotNull(capturedPrompt);
        Assert.DoesNotContain("Recent Activities", capturedPrompt);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static TaskItem MakeTask( string description
                                    , bool   isImportant = false
                                    , bool   isUrgent    = false )
        => new()
           {
                   ShortDescription = description
                 , IsImportant      = isImportant
                 , IsUrgent         = isUrgent
           };

    private static JournalEntryWithRevision MakeEntry(string text)
    {
        var entryId  = Guid.NewGuid().ToString("N");
        var entry    = new JournalEntry { Id = entryId };
        var revision = new JournalRevision
                       {
                               RevisionId = Guid.NewGuid().ToString("N")
                             , EntryId    = entryId
                             , CreatedUtc = DateTimeOffset.UtcNow
                             , Text       = text
                       };
        return new JournalEntryWithRevision(entry, revision, IsEdited: false);
    }
}
