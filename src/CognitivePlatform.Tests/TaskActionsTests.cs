using Moq;
using CognitivePlatform.Api.Domains.Tasks;

namespace CognitivePlatform.Tests;

public class TaskActionsTests
{
    private readonly Mock<ITaskService>       _taskServiceMock = new();
    private readonly Mock<IDailyBriefService> _dailyBriefMock  = new();
    private readonly TaskActions              _actions;

    public TaskActionsTests()
    {
        _actions = new TaskActions(_taskServiceMock.Object, _dailyBriefMock.Object);
    }

    // BUG-10 regression: DeleteTask was returning the not-found string instead
    // of throwing, so ExecutionEngine logged AuditOutcome.Success for a failed
    // delete. After the fix, DeleteTask throws InvalidOperationException so the
    // catch block in ExecutionEngine records AuditOutcome.Failure.

    [Fact]
    public void DeleteTask_Throws_WhenTaskPositionNotFound()
    {
        _taskServiceMock.Setup(service => service.ResolveByPosition(It.IsAny<int>()))
                        .Returns((TaskItem?)null);

        var ex = Assert.Throws<InvalidOperationException>(() => _actions.DeleteTask("999"));

        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void DeleteTask_ReturnsConfirmation_WhenTaskExists()
    {
        var task = new TaskItem
                   {
                           Id               = Guid.NewGuid().ToString()
                         , ShortDescription = "Buy milk"
                   };

        _taskServiceMock.Setup(service => service.ResolveByPosition(2))
                        .Returns(task);

        _taskServiceMock.Setup(service => service.Delete(task.Id));

        var result = _actions.DeleteTask("2");

        Assert.Contains("Buy milk", result);
        _taskServiceMock.Verify(service => service.Delete(task.Id), Times.Once);
    }

    // BUG-12 NL follow-through: GetDailyBrief must supply the user's local date to
    // the brief service. Without this, the server falls back to UtcNow.Date and the
    // calendar-window / due-today bug fixed in BUG-12 keeps biting every NL caller.
    [Fact]
    public void GetDailyBrief_PassesTodaysLocalDate_ToBriefService()
    {
        DateOnly? capturedDate = null;
        _dailyBriefMock.Setup(brief => brief.GetBrief(It.IsAny<DateOnly?>()))
                       .Callback<DateOnly?>(date => capturedDate = date)
                       .Returns("brief");

        var before = DateOnly.FromDateTime(DateTime.Now);
        _actions.GetDailyBrief();
        var after  = DateOnly.FromDateTime(DateTime.Now);

        Assert.NotNull(capturedDate);

        // Midnight-crossing tolerance: the action could have captured either the
        // date it was before the call, or after Ã¢â‚¬â€ if the wall clock rolled over.
        Assert.True(capturedDate == before || capturedDate == after
                  , $"Captured {capturedDate}, expected {before} or {after}.");
    }

    // G1 regression: AddTask must not surface the GUID in its return string.
    [Fact]
    public void AddTask_ReturnsDescriptionOnly_NotId()
    {
        _taskServiceMock.Setup(service => service.Create(It.IsAny<TaskItem>()));

        var result = _actions.AddTask("Buy groceries");

        Assert.Equal("Task created: Buy groceries", result);
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", result);
    }

    // BUG-22: ListCompletedTasks must return only completed tasks, not open ones.

    [Fact]
    public void ListCompletedTasks_ReturnsCompletedTask_WhenOneExists()
    {
        var completedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var task = new TaskItem
                   {
                           Id               = Guid.NewGuid().ToString()
                         , ShortDescription = "Write report"
                         , CompletedAt      = completedAt
                   };

        _taskServiceMock.Setup(service => service.GetCompleted())
                        .Returns(new List<TaskItem> { task });

        var result = _actions.ListCompletedTasks();

        Assert.Contains("Write report", result);
        Assert.Contains("Completed Tasks (1)", result);
    }

    [Fact]
    public void ListCompletedTasks_DoesNotInclude_OpenTasks()
    {
        var openTask = new TaskItem
                       {
                               Id               = Guid.NewGuid().ToString()
                             , ShortDescription = "Open task"
                             , CompletedAt      = null
                       };
        var completedTask = new TaskItem
                            {
                                    Id               = Guid.NewGuid().ToString()
                                  , ShortDescription = "Done task"
                                  , CompletedAt      = DateTimeOffset.UtcNow.AddHours(-1)
                            };

        _taskServiceMock.Setup(service => service.GetCompleted())
                        .Returns(new List<TaskItem> { completedTask });

        var result = _actions.ListCompletedTasks();

        Assert.Contains("Done task", result);
        Assert.DoesNotContain("Open task", result);
    }

    [Fact]
    public void ListCompletedTasks_ReturnsNoTasksMessage_WhenNoneExist()
    {
        _taskServiceMock.Setup(service => service.GetCompleted())
                        .Returns(new List<TaskItem>());

        var result = _actions.ListCompletedTasks();

        Assert.Equal("No completed tasks found.", result);
    }

    // G2 regression: ListTasks must not emit a "**Id:**" field for any task.
    [Fact]
    public void ListTasks_DoesNotInclude_IdField()
    {
        var task = new TaskItem
                   {
                           Id               = Guid.NewGuid().ToString()
                         , ShortDescription = "Buy groceries"
                   };

        _taskServiceMock.Setup(service => service.QueryTasks( It.IsAny<bool?>()
                                                            , It.IsAny<bool?>()
                                                            , It.IsAny<bool?>()
                                                            , It.IsAny<string?>()))
                        .Returns(new List<TaskItem> { task });

        _taskServiceMock.Setup(service => service.GetOrderedActiveTasks())
                        .Returns(new List<(int Position, TaskItem Task)> { (1, task) });

        var result = _actions.ListTasks();

        Assert.Contains("Buy groceries", result);
        Assert.Contains("## 1.", result);
        Assert.DoesNotContain("**Id:**", result);
    }

    [Fact]
    public void DeleteTask_DeletesByName_WhenSingleMatchFound()
    {
        var task = new TaskItem
                   {
                           Id               = Guid.NewGuid().ToString("N")
                         , ShortDescription = "buying groceries"
                   };

        _taskServiceMock.Setup(service => service.GetOrderedActiveTasks())
                        .Returns(new List<(int Position, TaskItem Task)> { (1, task) });

        _taskServiceMock.Setup(service => service.Delete(task.Id));

        var result = _actions.DeleteTask("buying groceries");

        Assert.Contains("buying groceries", result);
        _taskServiceMock.Verify(service => service.Delete(task.Id), Times.Once);
    }

    [Fact]
    public void DeleteTask_ThrowsAmbiguousError_WhenMultipleMatchesFound()
    {
        var task1 = new TaskItem
                    {
                            Id               = Guid.NewGuid().ToString("N")
                          , ShortDescription = "buying groceries at store"
                    };
        var task2 = new TaskItem
                    {
                            Id               = Guid.NewGuid().ToString("N")
                          , ShortDescription = "buying groceries online"
                    };

        _taskServiceMock.Setup(service => service.GetOrderedActiveTasks())
                        .Returns(new List<(int Position, TaskItem Task)> { (1, task1), (2, task2) });

        var ex = Assert.Throws<InvalidOperationException>(() => _actions.DeleteTask("buying groceries"));

        Assert.Contains("Multiple tasks matched", ex.Message);
        Assert.Contains("position 1", ex.Message);
        Assert.Contains("position 2", ex.Message);
    }

    [Fact]
    public void DeleteTask_ThrowsNotFoundError_WhenNonGuidStringHasNoMatch()
    {
        _taskServiceMock.Setup(service => service.GetOrderedActiveTasks())
                        .Returns(new List<(int Position, TaskItem Task)>());

        var ex = Assert.Throws<InvalidOperationException>(() => _actions.DeleteTask("nonexistent task"));

        Assert.Contains("No active task found matching 'nonexistent task'", ex.Message);
    }
}
