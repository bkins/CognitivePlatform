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
        // date it was before the call, or after — if the wall clock rolled over.
        Assert.True(capturedDate == before || capturedDate == after
                  , $"Captured {capturedDate}, expected {before} or {after}.");
    }
}
