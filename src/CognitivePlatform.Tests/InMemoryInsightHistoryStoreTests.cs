using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

public class InMemoryInsightHistoryStoreTests
{
    private readonly InMemoryInsightHistoryStore _store = new();

    private static Insight Make(string deduplicationKey
                              , InsightCategory category = InsightCategory.General) =>
        new()
        {
                Message          = $"Insight for {deduplicationKey}"
              , DeduplicationKey = deduplicationKey
              , Category         = category
        };

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsFalse_WhenStoreIsEmpty()
    {
        var seen = await _store.WasRecentlyEmittedAsync("any.key", TimeSpan.FromMinutes(1));

        Assert.False(seen);
    }

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsTrue_WithinWindow_AfterRecord()
    {
        await _store.RecordEmittedAsync(new[] { Make("freshly.recorded") });

        var seen = await _store.WasRecentlyEmittedAsync("freshly.recorded", TimeSpan.FromHours(1));

        Assert.True(seen);
    }

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsFalse_OutsideWindow()
    {
        // Window of zero — anything older than "right now" is out of window.
        // The store only stamps DateTime.UtcNow at write time, so by the time we ask
        // a tiny epsilon has passed and the entry is older than zero.
        await _store.RecordEmittedAsync(new[] { Make("aged.out") });

        await Task.Delay(5);

        var seen = await _store.WasRecentlyEmittedAsync("aged.out", TimeSpan.Zero);

        Assert.False(seen);
    }

    [Fact]
    public async Task RecordEmittedAsync_OverwritesPreviousEntry_ForSameKey()
    {
        await _store.RecordEmittedAsync(new[] { Make("repeating.key") });
        var firstWrite = (await _store.GetRecentAsync(TimeSpan.FromHours(1))).Single();

        // Wait long enough that the new EmittedAt is unambiguously later.
        await Task.Delay(10);

        await _store.RecordEmittedAsync(new[] { Make("repeating.key") });
        var secondWrite = (await _store.GetRecentAsync(TimeSpan.FromHours(1))).Single();

        Assert.Equal(firstWrite.DeduplicationKey, secondWrite.DeduplicationKey);
        Assert.True(secondWrite.EmittedAt > firstWrite.EmittedAt
                  , $"Expected second write ({secondWrite.EmittedAt:O}) to be later than first ({firstWrite.EmittedAt:O}).");
    }

    [Fact]
    public async Task RecordEmittedAsync_SkipsEntriesWithEmptyDeduplicationKey()
    {
        var withKey    = Make("has.key");
        var withoutKey = new Insight { Message = "no key", DeduplicationKey = string.Empty };

        await _store.RecordEmittedAsync(new[] { withKey, withoutKey });

        var recent = await _store.GetRecentAsync(TimeSpan.FromHours(1));

        Assert.Single(recent);
        Assert.Equal("has.key", recent[0].DeduplicationKey);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsOnlyEntriesWithinWindow_NewestFirst()
    {
        await _store.RecordEmittedAsync(new[] { Make("alpha") });
        await Task.Delay(5);
        await _store.RecordEmittedAsync(new[] { Make("beta") });
        await Task.Delay(5);
        await _store.RecordEmittedAsync(new[] { Make("gamma") });

        var recent = await _store.GetRecentAsync(TimeSpan.FromHours(1));

        Assert.Equal(3, recent.Count);
        Assert.Equal("gamma", recent[0].DeduplicationKey);
        Assert.Equal("beta",  recent[1].DeduplicationKey);
        Assert.Equal("alpha", recent[2].DeduplicationKey);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCancellation()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await _store.GetRecentAsync(TimeSpan.FromHours(1), cts.Token);
        });
    }
}
