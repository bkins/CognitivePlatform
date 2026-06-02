using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for <see cref="ObjectStoreTelemetryAggregatorService"/> backed by
/// an in-memory SQLite database so no file system is touched.
/// </summary>
public sealed class ObjectStoreTelemetryAggregatorServiceTests : IDisposable
{
    private readonly SqliteConnection                     _persistentConnection;
    private readonly SqliteObjectStore                    _objectStore;
    private readonly ObjectStoreTelemetryAggregatorService _service;

    private const string PartitionKey = "telemetry";

    public ObjectStoreTelemetryAggregatorServiceTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _objectStore = new SqliteObjectStore(connectionString);
        _service     = new ObjectStoreTelemetryAggregatorService(_objectStore);
    }

    public void Dispose() => _persistentConnection.Dispose();

    [Fact]
    public async Task GetAggregatedMetricsAsync_ReturnsEmpty_WhenNoRecordsExist()
    {
        var result = await _service.GetAggregatedMetricsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_ReturnsOneGroup_WhenOnlyOneEventNameStored()
    {
        await _objectStore.Save(new TelemetryRecord
                                {
                                        EventName  = "Converse.Ended"
                                      , SessionId  = "s1"
                                      , DurationMs = 500.0
                                      , Success    = true
                                }
                               , partitionKey: PartitionKey);

        var result = await _service.GetAggregatedMetricsAsync();

        Assert.Single(result);
        Assert.Equal("Converse.Ended", result[0].OperationName);
        Assert.Equal(1,     result[0].Count);
        Assert.Equal(500.0, result[0].AverageDurationMs);
        Assert.Equal(1.0,   result[0].SuccessRate);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_ComputesCorrectAverage_ForMultipleRecords()
    {
        foreach (var ms in new[] { 100.0, 200.0, 300.0 })
        {
            await _objectStore.Save(new TelemetryRecord
                                    {
                                            EventName  = "Converse.Ended"
                                          , DurationMs = ms
                                          , Success    = true
                                    }
                                   , partitionKey: PartitionKey);
        }

        var result = await _service.GetAggregatedMetricsAsync();

        var group = Assert.Single(result);
        Assert.Equal(3,     group.Count);
        Assert.Equal(200.0, group.AverageDurationMs);
        Assert.Equal(100.0, group.MinDurationMs);
        Assert.Equal(300.0, group.MaxDurationMs);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_ComputesCorrectSuccessRate_WhenMixedResults()
    {
        await _objectStore.Save(new TelemetryRecord { EventName = "Converse.Ended", Success = true  }, partitionKey: PartitionKey);
        await _objectStore.Save(new TelemetryRecord { EventName = "Converse.Ended", Success = true  }, partitionKey: PartitionKey);
        await _objectStore.Save(new TelemetryRecord { EventName = "Converse.Ended", Success = false }, partitionKey: PartitionKey);

        var result = await _service.GetAggregatedMetricsAsync();

        var group = Assert.Single(result);
        Assert.Equal(3,              group.Count);
        Assert.Equal(2.0 / 3.0,      group.SuccessRate, precision: 5);
    }

    [Fact]
    public async Task GetAggregatedMetricsAsync_GroupsByEventName()
    {
        await _objectStore.Save(new TelemetryRecord { EventName = "Converse.Ended",    DurationMs = 100 }, partitionKey: PartitionKey);
        await _objectStore.Save(new TelemetryRecord { EventName = "Execution.Started", DurationMs = 50  }, partitionKey: PartitionKey);

        var result = await _service.GetAggregatedMetricsAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.OperationName == "Converse.Ended");
        Assert.Contains(result, r => r.OperationName == "Execution.Started");
    }
}
