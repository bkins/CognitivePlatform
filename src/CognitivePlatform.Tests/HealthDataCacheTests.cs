using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Tests;

public class HealthDataCacheTests
{
    private static HealthSnapshot MakeSnapshot(DateOnly date, long steps = 5000)
        => new()
           {
               Date             = date
             , Steps            = steps
             , DistanceMetres   = 3800
             , AverageHeartRate = 72
             , MinHeartRate     = 55
             , MaxHeartRate     = 120
             , SleepMinutes     = 420
             , SleepSessions    = 1
             , Platform         = "Android"
           };

    // -------------------------------------------------------------------
    // Store / TryGet — basic round-trip
    // -------------------------------------------------------------------

    [Fact]
    public void TryGet_ReturnsFalse_WhenNoEntryStored()
    {
        var cache = new HealthDataCache();

        var result = cache.TryGet(DateOnly.FromDateTime(DateTime.Today), out var snapshot);

        Assert.False(result);
        Assert.Null(snapshot);
    }

    [Fact]
    public void TryGet_ReturnsTrue_AfterStoreForSameDate()
    {
        var cache = new HealthDataCache();
        var date  = DateOnly.FromDateTime(DateTime.Today);
        var stored = MakeSnapshot(date, steps: 8_000);

        cache.Store(stored);
        var result = cache.TryGet(date, out var retrieved);

        Assert.True(result);
        Assert.NotNull(retrieved);
        Assert.Equal(8_000, retrieved!.Steps);
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForDifferentDate()
    {
        var cache     = new HealthDataCache();
        var today     = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);

        cache.Store(MakeSnapshot(today));

        var result = cache.TryGet(yesterday, out var snapshot);

        Assert.False(result);
        Assert.Null(snapshot);
    }

    [Fact]
    public void Store_Overwrites_ExistingEntryForSameDate()
    {
        var cache = new HealthDataCache();
        var date  = DateOnly.FromDateTime(DateTime.Today);

        cache.Store(MakeSnapshot(date, steps: 1_000));
        cache.Store(MakeSnapshot(date, steps: 9_999));

        cache.TryGet(date, out var retrieved);

        Assert.Equal(9_999, retrieved!.Steps);
    }

    // -------------------------------------------------------------------
    // GetAge
    // -------------------------------------------------------------------

    [Fact]
    public void GetAge_ReturnsNull_WhenNoEntry()
    {
        var cache = new HealthDataCache();

        var age = cache.GetAge(DateOnly.FromDateTime(DateTime.Today));

        Assert.Null(age);
    }

    [Fact]
    public void GetAge_ReturnsSmallPositiveValue_ImmediatelyAfterStore()
    {
        var cache = new HealthDataCache();
        var date  = DateOnly.FromDateTime(DateTime.Today);

        cache.Store(MakeSnapshot(date));
        var age = cache.GetAge(date);

        Assert.NotNull(age);
        Assert.True(age!.Value.TotalSeconds < 5, $"Expected age < 5s, got {age.Value.TotalSeconds}s");
    }

    // -------------------------------------------------------------------
    // Thread safety
    // -------------------------------------------------------------------

    [Fact]
    public void Store_And_TryGet_AreThreadSafe()
    {
        var cache = new HealthDataCache();
        var date  = DateOnly.FromDateTime(DateTime.Today);

        var tasks = Enumerable.Range(0, 50)
                              .Select(index => Task.Run(() =>
                              {
                                  cache.Store(MakeSnapshot(date, steps: index));
                                  cache.TryGet(date, out _);
                              }))
                              .ToArray();

        var ex = Record.Exception(() => Task.WaitAll(tasks));

        Assert.Null(ex);
    }
}
