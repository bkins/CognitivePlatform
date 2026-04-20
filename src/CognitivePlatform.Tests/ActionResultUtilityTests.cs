using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Tests;

public class ActionResultUtilityTests
{
    [Fact]
    public async Task UnwrapAsync_ReturnsNull_WhenResultIsNull()
    {
        var result = await ActionResultUtility.UnwrapAsync(null);

        Assert.Null(result);
    }

    [Fact]
    public async Task UnwrapAsync_ReturnsActionResult_WhenResultIsActionResult()
    {
        var actionResult = new ActionResult { Success = true, Message = "ok" };

        var result = await ActionResultUtility.UnwrapAsync(actionResult);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("ok", result.Message);
    }

    [Fact]
    public async Task UnwrapAsync_ReturnsNull_WhenResultIsNonGenericTask()
    {
        var task = Task.CompletedTask;

        var result = await ActionResultUtility.UnwrapAsync(task);

        Assert.Null(result);
    }

    [Fact]
    public async Task UnwrapAsync_ReturnsActionResult_WhenResultIsGenericTask()
    {
        var actionResult = new ActionResult { Success = true, Message = "async-ok" };
        var task         = Task.FromResult(actionResult);

        var result = await ActionResultUtility.UnwrapAsync(task);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("async-ok", result.Message);
    }
}
