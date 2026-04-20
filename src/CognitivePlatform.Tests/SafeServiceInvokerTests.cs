using System.Reflection;
using CognitivePlatform.Api.Execution;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

public class SafeServiceInvokerTests
{
    private readonly SafeServiceInvoker _invoker = new();

    private static MethodInfo Method(string name)
        => typeof(StubService).GetMethod(name)!;

    private static readonly StubService Stub = new();

    // ================================================================
    // Successful invocation
    // ================================================================

    [Fact]
    public async Task InvokeAsync_ReturnsActionResult_WhenMethodSucceeds()
    {
        var method = Method(nameof(StubService.ReturnsSuccess));

        var result = await _invoker.InvokeAsync(method, Stub, [], dryRun: false);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Message);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsActionResult_WhenMethodReturnsTask()
    {
        var method = Method(nameof(StubService.ReturnsSuccessAsync));

        var result = await _invoker.InvokeAsync(method, Stub, [], dryRun: false);

        Assert.True(result.Success);
        Assert.Equal("async-ok", result.Message);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsNoResultMessage_WhenMethodReturnsNull()
    {
        var method = Method(nameof(StubService.ReturnsNull));

        var result = await _invoker.InvokeAsync(method, Stub, [], dryRun: false);

        Assert.Equal("No result returned.", result.Message);
    }

    // ================================================================
    // Exception handling
    // ================================================================

    [Fact]
    public async Task InvokeAsync_ReturnsErrorMessage_WhenTimeoutExceptionThrown()
    {
        var method = Method(nameof(StubService.ThrowsTimeout));

        var result = await _invoker.InvokeAsync(method, Stub, [], dryRun: false);

        Assert.False(result.Success);
        Assert.Contains("Database unavailable", result.Message);
        Assert.NotEmpty(result.Logs);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsErrorMessage_WhenUnknownExceptionThrown()
    {
        var method = Method(nameof(StubService.ThrowsUnknown));

        var result = await _invoker.InvokeAsync(method, Stub, [], dryRun: false);

        Assert.False(result.Success);
        Assert.Contains("Unknown error", result.Message);
        Assert.NotEmpty(result.Logs);
    }

    // ================================================================
    // Stub service used by reflection in tests above
    // ================================================================

    private class StubService
    {
        public ActionResult ReturnsSuccess()
            => new() { Success = true, Message = "ok" };

        public Task<ActionResult> ReturnsSuccessAsync()
            => Task.FromResult(new ActionResult { Success = true, Message = "async-ok" });

        public ActionResult? ReturnsNull()
            => null;

        public ActionResult ThrowsTimeout()
            => throw new TimeoutException("timed out");

        public ActionResult ThrowsUnknown()
            => throw new InvalidOperationException("something bad");
    }
}
