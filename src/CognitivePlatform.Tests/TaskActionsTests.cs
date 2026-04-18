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
}
