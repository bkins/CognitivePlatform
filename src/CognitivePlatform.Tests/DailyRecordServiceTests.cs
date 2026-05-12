using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using Moq;

namespace CognitivePlatform.Tests;

public class DailyRecordServiceTests
{
    private readonly Mock<IObjectStore>    _storeMock    = new();
    private readonly Mock<ITaskService>    _taskMock     = new();
    private readonly Mock<IJournalService> _journalMock  = new();
    private readonly DailyRecordService    _service;

    private static readonly string TodayKey
        = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");

    public DailyRecordServiceTests()
    {
        _journalMock
            .Setup(journal => journal.AddEntryAsync( It.IsAny<string>()
                                                   , It.IsAny<IReadOnlyList<string>>()
                                                   , It.IsAny<string?>()
                                                   , It.IsAny<int?>()
                                                   , It.IsAny<int?>()
                                                   , It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync("journal-entry-id");

        _storeMock
            .Setup(store => store.Save<DailyRecord>(   It.IsAny<DailyRecord>()
                                                     , It.IsAny<string?>()
                                                     , It.IsAny<string?>()))
            .ReturnsAsync((DailyRecord record, string? _, string? id) => id ?? record.Id);

        _storeMock
            .Setup(store => store.Save<DailyCheckpoint>(   It.IsAny<DailyCheckpoint>()
                                                         , It.IsAny<string?>()
                                                         , It.IsAny<string?>()))
            .ReturnsAsync("checkpoint-id");

        _service = new DailyRecordService(_storeMock.Object
                                        , _taskMock.Object
                                        , _journalMock.Object);
    }

    // ================================================================
    // OpenDayAsync — fresh open
    // ================================================================

    [Fact]
    public async Task OpenDayAsync_ReturnsRecord_WithOpeningPhase()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        var result = await _service.OpenDayAsync("Focused day.", Array.Empty<string>());

        Assert.Equal(DayPhase.Opening, result.Phase);
        Assert.Equal(TodayKey,         result.Id);
    }

    [Fact]
    public async Task OpenDayAsync_SetsOpeningText_AndJournalId()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        var result = await _service.OpenDayAsync("Deep work morning.", Array.Empty<string>());

        Assert.Equal("Deep work morning.", result.OpeningText);
        Assert.Equal("journal-entry-id",   result.OpeningJournalId);
    }

    [Fact]
    public async Task OpenDayAsync_CreatesTasks_WhenTaskTitlesProvided()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        _taskMock
            .SetupSequence(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Returns(new TaskItem { Id = "task-1", ShortDescription = "Fix bug" })
            .Returns(new TaskItem { Id = "task-2", ShortDescription = "Write tests" });

        var result = await _service.OpenDayAsync( "Productive day."
                                                , new[] { "Fix bug", "Write tests" });

        Assert.Equal(2,        result.PlannedTaskIds.Count);
        Assert.Contains("task-1", result.PlannedTaskIds);
        Assert.Contains("task-2", result.PlannedTaskIds);
    }

    [Fact]
    public async Task OpenDayAsync_DoesNotCreateJournalEntry_WhenRecordAlreadyOpen()
    {
        var existing = new DailyRecord { Id = TodayKey, Phase = DayPhase.Opening };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(existing);

        _taskMock
            .Setup(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Returns(new TaskItem { Id = "task-3", ShortDescription = "New task" });

        await _service.OpenDayAsync("Amended plan.", new[] { "New task" });

        _journalMock.Verify(journal => journal.AddEntryAsync( It.IsAny<string>()
                                                            , It.IsAny<IReadOnlyList<string>>()
                                                            , It.IsAny<string?>()
                                                            , It.IsAny<int?>()
                                                            , It.IsAny<int?>()
                                                            , It.IsAny<IReadOnlyList<string>>())
                          , Times.Never);
    }

    [Fact]
    public async Task OpenDayAsync_AppendsTasksToExistingRecord()
    {
        var existing = new DailyRecord
                       {
                               Id            = TodayKey
                             , Phase         = DayPhase.Active
                             , PlannedTaskIds = new List<string> { "existing-task" }
                       };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(existing);

        _taskMock
            .Setup(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Returns(new TaskItem { Id = "new-task", ShortDescription = "New item" });

        var result = await _service.OpenDayAsync("Extra work.", new[] { "New item" });

        Assert.Equal(2,               result.PlannedTaskIds.Count);
        Assert.Contains("existing-task", result.PlannedTaskIds);
        Assert.Contains("new-task",      result.PlannedTaskIds);
    }

    [Fact]
    public async Task OpenDayAsync_Throws_WhenDayIsAlreadyClosed()
    {
        var closed = new DailyRecord { Id = TodayKey, Phase = DayPhase.Closed };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(closed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.OpenDayAsync("Too late.", Array.Empty<string>()));
    }

    // ================================================================
    // AddCheckpointAsync
    // ================================================================

    [Fact]
    public async Task AddCheckpointAsync_ReturnsCheckpoint_WithText()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Opening };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var result = await _service.AddCheckpointAsync("Good progress this morning.");

        Assert.Equal("Good progress this morning.", result.Text);
        Assert.Equal(TodayKey,                      result.DailyRecordId);
    }

    [Fact]
    public async Task AddCheckpointAsync_TransitionsPhase_FromOpeningToActive()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Opening };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        await _service.AddCheckpointAsync("First check-in.");

        Assert.Equal(DayPhase.Active, record.Phase);
    }

    [Fact]
    public async Task AddCheckpointAsync_DoesNotDowngrade_WhenAlreadyActive()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Active };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        await _service.AddCheckpointAsync("Second check-in.");

        Assert.Equal(DayPhase.Active, record.Phase);
    }

    [Fact]
    public async Task AddCheckpointAsync_MarksTasksComplete_WhenIdsProvided()
    {
        var record = new DailyRecord
                     {
                             Id    = TodayKey
                           , Phase = DayPhase.Active
                     };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var completedIds = new[] { "task-a", "task-b" };

        var result = await _service.AddCheckpointAsync( "Done a few things."
                                                       , completedTaskIds: completedIds);

        _taskMock.Verify(taskService => taskService.Complete("task-a"), Times.Once);
        _taskMock.Verify(taskService => taskService.Complete("task-b"), Times.Once);
        Assert.Equal(2,         result.CompletedTaskIds.Count);
        Assert.Contains("task-a", result.CompletedTaskIds);
    }

    [Fact]
    public async Task AddCheckpointAsync_AddsReactiveTasks_AndTracksIds()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Active };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        _taskMock
            .Setup(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Returns(new TaskItem { Id = "reactive-1", ShortDescription = "Unplanned meeting" });

        var result = await _service.AddCheckpointAsync( "Unplanned meeting came up."
                                                       , newTaskTitles: new[] { "Unplanned meeting" });

        Assert.Single(result.AddedTaskIds);
        Assert.Contains("reactive-1", result.AddedTaskIds);
        Assert.Contains("reactive-1", record.ReactiveTaskIds);
    }

    [Fact]
    public async Task AddCheckpointAsync_Throws_WhenNoRecordForToday()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AddCheckpointAsync("No plan exists."));
    }

    [Fact]
    public async Task AddCheckpointAsync_Throws_WhenDayIsClosed()
    {
        var closed = new DailyRecord { Id = TodayKey, Phase = DayPhase.Closed };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(closed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AddCheckpointAsync("Too late."));
    }

    // ================================================================
    // CloseDayAsync
    // ================================================================

    [Fact]
    public async Task CloseDayAsync_SetsPhase_ToClosed()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Active };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var result = await _service.CloseDayAsync("Good day.");

        Assert.Equal(DayPhase.Closed, result.Phase);
    }

    [Fact]
    public async Task CloseDayAsync_SetsClosingText_AndJournalId()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Active };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var result = await _service.CloseDayAsync("Solid day.");

        Assert.Equal("Solid day.",       result.ClosingText);
        Assert.Equal("journal-entry-id", result.ClosingJournalId);
    }

    [Fact]
    public async Task CloseDayAsync_ComputesCompletionRate_WhenSomeTasksCompleted()
    {
        var record = new DailyRecord
                     {
                             Id            = TodayKey
                           , Phase         = DayPhase.Active
                           , PlannedTaskIds = new List<string> { "t1", "t2", "t3" }
                     };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        _taskMock.Setup(taskService => taskService.Get("t1"))
                 .Returns(new TaskItem { Id = "t1", CompletedAt = DateTimeOffset.UtcNow });
        _taskMock.Setup(taskService => taskService.Get("t2"))
                 .Returns(new TaskItem { Id = "t2", CompletedAt = null });
        _taskMock.Setup(taskService => taskService.Get("t3"))
                 .Returns(new TaskItem { Id = "t3", CompletedAt = DateTimeOffset.UtcNow });

        var result = await _service.CloseDayAsync("Done for today.");

        Assert.Equal(3,            result.PlannedTaskCount);
        Assert.Equal(2,            result.CompletedTaskCount);
        Assert.Equal(2.0 / 3.0,   result.CompletionRate, precision: 10);
    }

    [Fact]
    public async Task CloseDayAsync_CompletionRate_IsZero_WhenNoPlannedTasks()
    {
        var record = new DailyRecord
                     {
                             Id    = TodayKey
                           , Phase = DayPhase.Opening
                     };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var result = await _service.CloseDayAsync("Quick day.");

        Assert.Equal(0.0, result.CompletionRate);
    }

    [Fact]
    public async Task CloseDayAsync_TagsUncompletedTasks_AsRolledOver()
    {
        var record = new DailyRecord
                     {
                             Id            = TodayKey
                           , Phase         = DayPhase.Active
                           , PlannedTaskIds = new List<string> { "done-task", "pending-task" }
                     };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var doneTask    = new TaskItem { Id = "done-task",    CompletedAt = DateTimeOffset.UtcNow };
        var pendingTask = new TaskItem { Id = "pending-task", CompletedAt = null };

        _taskMock.Setup(taskService => taskService.Get("done-task"))   .Returns(doneTask);
        _taskMock.Setup(taskService => taskService.Get("pending-task")).Returns(pendingTask);

        await _service.CloseDayAsync("Closing out.");

        _taskMock.Verify(taskService => taskService.Update(It.Is<TaskItem>(
            task => task.Id == "pending-task"
                 && task.Tags.Contains($"rolled-over:{TodayKey}")))
                       , Times.Once);

        _taskMock.Verify(taskService => taskService.Update(It.Is<TaskItem>(
            task => task.Id == "done-task"))
                       , Times.Never);
    }

    [Fact]
    public async Task CloseDayAsync_Throws_WhenNoRecordForToday()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CloseDayAsync("Closing non-existent day."));
    }

    // ================================================================
    // ClaimRolledOverTasksAsync
    // ================================================================

    [Fact]
    public async Task ClaimRolledOverTasksAsync_AddsRolledOverTasks_ToPlannedIds()
    {
        var record = new DailyRecord
                     {
                             Id            = TodayKey
                           , Phase         = DayPhase.Opening
                           , PlannedTaskIds = new List<string>()
                     };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var rolledTask = new TaskItem
                         {
                                 Id           = "rolled-1"
                               , Tags         = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rolled-over:2026-04-12" }
                               , CompletedAt  = null
                         };
        _taskMock.Setup(taskService => taskService.List(null, null, false))
                 .Returns(new List<TaskItem> { rolledTask });

        var result = await _service.ClaimRolledOverTasksAsync();

        Assert.Contains("rolled-1", result.PlannedTaskIds);
    }

    [Fact]
    public async Task ClaimRolledOverTasksAsync_DoesNotDuplicate_WhenAlreadyClaimed()
    {
        var record = new DailyRecord
                     {
                             Id            = TodayKey
                           , Phase         = DayPhase.Opening
                           , PlannedTaskIds = new List<string> { "rolled-1" }
                     };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var rolledTask = new TaskItem
                         {
                                 Id    = "rolled-1"
                               , Tags  = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rolled-over:2026-04-12" }
                         };
        _taskMock.Setup(taskService => taskService.List(null, null, false))
                 .Returns(new List<TaskItem> { rolledTask });

        var result = await _service.ClaimRolledOverTasksAsync();

        Assert.Single(result.PlannedTaskIds);
    }

    // ================================================================
    // GetRolledOverTasks
    // ================================================================

    [Fact]
    public void GetRolledOverTasks_ReturnsOnlyTasksWithRolledOverTag()
    {
        var rolledTask  = new TaskItem { Id = "r1", Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rolled-over:2026-04-11" } };
        var regularTask = new TaskItem { Id = "r2", Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "work" } };

        _taskMock.Setup(taskService => taskService.List(null, null, false))
                 .Returns(new List<TaskItem> { rolledTask, regularTask });

        var result = _service.GetRolledOverTasks();

        Assert.Single(result);
        Assert.Equal("r1", result[0].Id);
    }

    [Fact]
    public void GetRolledOverTasks_ReturnsEmpty_WhenNoRolledOverTasks()
    {
        _taskMock.Setup(taskService => taskService.List(null, null, false))
                 .Returns(new List<TaskItem>());

        var result = _service.GetRolledOverTasks();

        Assert.Empty(result);
    }

    // ================================================================
    // GetToday / GetByDate
    // ================================================================

    [Fact]
    public void GetToday_ReturnsRecord_WhenStoreHasIt()
    {
        var record = new DailyRecord { Id = TodayKey };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        var result = _service.GetToday();

        Assert.NotNull(result);
        Assert.Equal(TodayKey, result.Id);
    }

    [Fact]
    public void GetToday_ReturnsNull_WhenNoRecordExists()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        var result = _service.GetToday();

        Assert.Null(result);
    }

    [Fact]
    public void GetByDate_ReturnsRecord_ForGivenDate()
    {
        var date   = new DateOnly(2026, 4, 1);
        var key    = "2026-04-01";
        var record = new DailyRecord { Id = key };

        _storeMock.Setup(store => store.Get<DailyRecord>(key, null)).Returns(record);

        var result = _service.GetByDate(date);

        Assert.NotNull(result);
        Assert.Equal(key, result.Id);
    }

    // ================================================================
    // OpenDayAsync — due date extraction (Bug 2 regression)
    // ================================================================

    [Fact]
    public async Task OpenDayAsync_SetsDueDate_WhenTitleContainsByTomorrow()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        TaskItem? capturedTask = null;

        _taskMock
            .Setup(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Callback<TaskItem>(taskItem => capturedTask = taskItem)
            .Returns<TaskItem>(taskItem => new TaskItem
                                           {
                                                   Id               = "task-due"
                                                 , ShortDescription = taskItem.ShortDescription
                                                 , DueDate          = taskItem.DueDate
                                           });

        await _service.OpenDayAsync("Plan day.", new[] { "Fix login bug by tomorrow" });

        Assert.NotNull(capturedTask);
        Assert.Equal("Fix login bug",                             capturedTask!.ShortDescription);
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(1), capturedTask.DueDate!.Value.Date);
    }

    [Fact]
    public async Task OpenDayAsync_LeavesCleanTitle_WhenNoDateSuffixPresent()
    {
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns((DailyRecord?)null);

        TaskItem? capturedTask = null;

        _taskMock
            .Setup(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Callback<TaskItem>(taskItem => capturedTask = taskItem)
            .Returns<TaskItem>(taskItem => new TaskItem
                                           {
                                                   Id               = "task-plain"
                                                 , ShortDescription = taskItem.ShortDescription
                                           });

        await _service.OpenDayAsync("Plan day.", new[] { "Review pull requests" });

        Assert.NotNull(capturedTask);
        Assert.Equal("Review pull requests", capturedTask!.ShortDescription);
        Assert.Null(capturedTask.DueDate);
    }

    [Fact]
    public async Task AddCheckpointAsync_SetsDueDate_WhenNewTaskTitleContainsByFriday()
    {
        var record = new DailyRecord { Id = TodayKey, Phase = DayPhase.Active };
        _storeMock.Setup(store => store.Get<DailyRecord>(TodayKey, null)).Returns(record);

        TaskItem? capturedTask = null;

        _taskMock
            .Setup(taskService => taskService.Create(It.IsAny<TaskItem>()))
            .Callback<TaskItem>(taskItem => capturedTask = taskItem)
            .Returns<TaskItem>(taskItem => new TaskItem
                                           {
                                                   Id               = "task-checkpoint"
                                                 , ShortDescription = taskItem.ShortDescription
                                                 , DueDate          = taskItem.DueDate
                                           });

        await _service.AddCheckpointAsync( "Discovered extra work."
                                          , newTaskTitles: new[] { "Write release notes by Friday" });

        Assert.NotNull(capturedTask);
        Assert.Equal("Write release notes", capturedTask!.ShortDescription);
        Assert.NotNull(capturedTask.DueDate);
        Assert.Equal(DayOfWeek.Friday,      capturedTask.DueDate!.Value.DayOfWeek);
    }
}
