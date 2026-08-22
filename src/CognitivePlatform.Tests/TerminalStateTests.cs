using System;
using CognitivePlatform.Admin.Services;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class TerminalStateTests
{
    [Fact]
    public void ElapsedDisplay_ReturnsEmpty_WhenNotRunning()
    {
        var service = new TerminalStateService();
        var state   = service.Get("t1");

        var display = state.ElapsedDisplay;

        Assert.Empty(display);
    }

    [Fact]
    public void ElapsedDisplay_FormatsElapsedWithoutTimeout_WhenTimeoutIsNull()
    {
        var service = new TerminalStateService();
        service.MarkRunning("t2", true);
        var state = service.Get("t2");

        var display = state.ElapsedDisplay;

        Assert.StartsWith("Running 0:", display);
        Assert.DoesNotContain("/", display);
    }

    [Fact]
    public void ElapsedDisplay_FormatsElapsedAndTimeout_WhenTimeoutIsSet()
    {
        var service = new TerminalStateService();
        service.MarkRunning("t3", true, TimeSpan.FromMinutes(15));
        var state = service.Get("t3");

        var display = state.ElapsedDisplay;

        Assert.StartsWith("Running 0:", display);
        Assert.EndsWith("/ 15m", display);
    }

    [Fact]
    public void MarkRunning_SetsTimeout_WhenProvided()
    {
        var service = new TerminalStateService();
        var timeout = TimeSpan.FromMinutes(20);

        service.MarkRunning("t4", true, timeout);
        var state = service.Get("t4");

        Assert.True(state.IsRunning);
        Assert.NotNull(state.StartedAt);
        Assert.Equal(timeout, state.Timeout);
    }
}
