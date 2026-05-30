using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Wellbeing;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests.Wellbeing;

/// <summary>
/// Integration-style tests for <see cref="WellbeingSignalStore"/> backed by an
/// in-memory SQLite database so no file system is touched.
/// </summary>
public sealed class WellbeingSignalStoreTests : IDisposable
{
    private readonly SqliteConnection    _persistentConnection;
    private readonly SqliteObjectStore   _objectStore;
    private readonly WellbeingSignalStore _store;

    public WellbeingSignalStoreTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _objectStore = new SqliteObjectStore(connectionString);
        _store       = new WellbeingSignalStore(_objectStore);
    }

    public void Dispose() => _persistentConnection.Dispose();

    private static WellbeingSignal MakeSignal(
        DateOnly             date
      , WellbeingSignalSource source     = WellbeingSignalSource.Tasks
      , string               metricName = WellbeingMetrics.TasksCompleted
      , double               value      = 3.0
      , string?              unit       = "count") =>
        new()
        {
                Id         = $"{source}.{metricName}.{date:yyyy-MM-dd}"
              , Date       = date
              , Source     = source
              , MetricName = metricName
              , Value      = value
              , Unit       = unit
              , CollectedAt = DateTimeOffset.UtcNow
        };

    // ====================================================================
    // SaveSignalAsync / GetSignalsForDateAsync
    // ====================================================================

    [Fact]
    public async Task SaveSignalAsync_And_GetSignalsForDateAsync_Round_Trip()
    {
        var date   = new DateOnly(2026, 5, 29);
        var signal = MakeSignal(date, value: 5.0);

        await _store.SaveSignalAsync(signal);

        var retrieved = await _store.GetSignalsForDateAsync(date);

        Assert.Single(retrieved);
        Assert.Equal(signal.Id,         retrieved[0].Id);
        Assert.Equal(signal.MetricName, retrieved[0].MetricName);
        Assert.Equal(5.0,               retrieved[0].Value);
    }

    [Fact]
    public async Task GetSignalsForDateAsync_ReturnsEmpty_WhenNoSignalsSaved()
    {
        var date = new DateOnly(2026, 1, 1);

        var retrieved = await _store.GetSignalsForDateAsync(date);

        Assert.Empty(retrieved);
    }

    [Fact]
    public async Task SaveSignalAsync_Upserts_ExistingSignal()
    {
        var date   = new DateOnly(2026, 5, 29);
        var first  = MakeSignal(date, value: 2.0);
        var second = first with { Value = 9.0 };

        await _store.SaveSignalAsync(first);
        await _store.SaveSignalAsync(second);

        var retrieved = await _store.GetSignalsForDateAsync(date);

        Assert.Single(retrieved);
        Assert.Equal(9.0, retrieved[0].Value);
    }

    [Fact]
    public async Task GetSignalsForDateAsync_DoesNotReturn_SignalsFromOtherDate()
    {
        var dateA = new DateOnly(2026, 5, 28);
        var dateB = new DateOnly(2026, 5, 29);

        await _store.SaveSignalAsync(MakeSignal(dateA));
        await _store.SaveSignalAsync(MakeSignal(dateB, value: 7.0));

        var retrieved = await _store.GetSignalsForDateAsync(dateA);

        Assert.Single(retrieved);
        Assert.Equal(dateA, retrieved[0].Date);
    }

    [Fact]
    public async Task GetSignalsForDateAsync_Returns_MultipleSignals_ForSameDate()
    {
        var date = new DateOnly(2026, 5, 29);

        await _store.SaveSignalAsync(MakeSignal(date, source: WellbeingSignalSource.Tasks  , metricName: WellbeingMetrics.TasksCompleted , value: 3.0));
        await _store.SaveSignalAsync(MakeSignal(date, source: WellbeingSignalSource.Journal, metricName: WellbeingMetrics.JournalEntryCount, value: 1.0));

        var retrieved = await _store.GetSignalsForDateAsync(date);

        Assert.Equal(2, retrieved.Count);
    }

    // ====================================================================
    // GetSignalsAsync (date range)
    // ====================================================================

    [Fact]
    public async Task GetSignalsAsync_Returns_Signals_AcrossDateRange()
    {
        var day1 = new DateOnly(2026, 5, 27);
        var day2 = new DateOnly(2026, 5, 28);
        var day3 = new DateOnly(2026, 5, 29);

        await _store.SaveSignalAsync(MakeSignal(day1));
        await _store.SaveSignalAsync(MakeSignal(day2));
        await _store.SaveSignalAsync(MakeSignal(day3));

        var from = new DateTimeOffset(day1.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to   = new DateTimeOffset(day3.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var retrieved = await _store.GetSignalsAsync(from, to);

        Assert.Equal(3, retrieved.Count);
    }

    [Fact]
    public async Task GetSignalsAsync_Excludes_Signals_OutsideDateRange()
    {
        var inRange  = new DateOnly(2026, 5, 28);
        var outRange = new DateOnly(2026, 5, 31);

        await _store.SaveSignalAsync(MakeSignal(inRange));
        await _store.SaveSignalAsync(MakeSignal(outRange));

        var from = new DateTimeOffset(new DateOnly(2026, 5, 27).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to   = new DateTimeOffset(new DateOnly(2026, 5, 29).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var retrieved = await _store.GetSignalsAsync(from, to);

        Assert.All(retrieved, signal => Assert.True(signal.Date >= new DateOnly(2026, 5, 27)));
        Assert.All(retrieved, signal => Assert.True(signal.Date <= new DateOnly(2026, 5, 29)));
        Assert.DoesNotContain(retrieved, signal => signal.Date == outRange);
    }

    [Fact]
    public async Task GetSignalsAsync_ReturnsEmpty_WhenNoSignalsInRange()
    {
        var from = new DateTimeOffset(new DateOnly(2026, 1, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to   = new DateTimeOffset(new DateOnly(2026, 1, 7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var retrieved = await _store.GetSignalsAsync(from, to);

        Assert.Empty(retrieved);
    }
}
