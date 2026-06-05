using CognitivePlatform.Api.Training;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

public class InterpreterTrainingStoreTests : IDisposable
{
    private readonly string                        _dbPath;
    private readonly SqliteInterpreterTrainingStore _store;

    public InterpreterTrainingStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"training_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate";
        _store = new SqliteInterpreterTrainingStore(connectionString);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    // -----------------------------------------------------------------------
    // GetCountAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCountAsync_ReturnsZero_WhenEmpty()
    {
        var count = await _store.GetCountAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetCountAsync_ReturnsCorrectCount_AfterMultipleSaves()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.SaveAsync(new InterpreterTrainingRecord
                                   {
                                           UserInput    = $"input {i}"
                                         , TimestampUtc = DateTime.UtcNow.AddSeconds(i)
                                   });
        }

        var count = await _store.GetCountAsync();

        Assert.Equal(5, count);
    }

    // -----------------------------------------------------------------------
    // SaveAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_PersistsRecord_VerifiableViaCount()
    {
        var record = new InterpreterTrainingRecord
                     {
                             Id                  = Guid.NewGuid()
                           , UserInput           = "add task finish report"
                           , NormalizedInput      = "add task finish report"
                           , ModelOutput          = """{"actionName":"AddTask","parameters":{"title":"finish report"}}"""
                           , FinalResolvedAction  = "AddTask"
                           , Parameters           = new Dictionary<string, string> { ["title"] = "finish report" }
                           , ExecutionSucceeded   = true
                           , TimestampUtc         = DateTime.UtcNow
                           , ModelVersion         = "llama3.1:8b"
                           , PromptVersion        = "system.txt"
                     };

        await _store.SaveAsync(record);

        var count = await _store.GetCountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveAsync_IsIdempotent_WhenSameIdSavedTwice()
    {
        var id     = Guid.NewGuid();
        var record = new InterpreterTrainingRecord { Id = id, UserInput = "test" };

        await _store.SaveAsync(record);
        await _store.SaveAsync(record);

        var count = await _store.GetCountAsync();
        Assert.Equal(1, count);
    }

    // -----------------------------------------------------------------------
    // GetRecentAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_ReturnsRecord_AfterSave()
    {
        var record = new InterpreterTrainingRecord
                     {
                             UserInput           = "journal: had a good day"
                           , NormalizedInput      = "journal: had a good day"
                           , ModelOutput          = """{"actionName":"CreateJournalEntry","parameters":{"text":"had a good day"}}"""
                           , FinalResolvedAction  = "CreateJournalEntry"
                           , ExecutionSucceeded   = true
                           , TimestampUtc         = DateTime.UtcNow
                           , ModelVersion         = "phi3:mini"
                           , PromptVersion        = "system.txt"
                     };

        await _store.SaveAsync(record);

        var recent = await _store.GetRecentAsync(10);

        Assert.Single(recent);
        Assert.Equal("CreateJournalEntry", recent[0].FinalResolvedAction);
        Assert.Equal("phi3:mini",          recent[0].ModelVersion);
        Assert.True(recent[0].ExecutionSucceeded);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsMostRecentFirst()
    {
        var older = new InterpreterTrainingRecord
                    {
                            UserInput           = "older input"
                          , FinalResolvedAction  = "OlderAction"
                          , TimestampUtc         = DateTime.UtcNow.AddMinutes(-5)
                    };
        var newer = new InterpreterTrainingRecord
                    {
                            UserInput           = "newer input"
                          , FinalResolvedAction  = "NewerAction"
                          , TimestampUtc         = DateTime.UtcNow
                    };

        await _store.SaveAsync(older);
        await _store.SaveAsync(newer);

        var recent = await _store.GetRecentAsync(10);

        Assert.Equal("NewerAction", recent[0].FinalResolvedAction);
        Assert.Equal("OlderAction", recent[1].FinalResolvedAction);
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCountLimit()
    {
        for (var i = 0; i < 15; i++)
        {
            await _store.SaveAsync(new InterpreterTrainingRecord
                                   {
                                           UserInput    = $"input {i}"
                                         , TimestampUtc = DateTime.UtcNow.AddSeconds(i)
                                   });
        }

        var recent = await _store.GetRecentAsync(5);

        Assert.Equal(5, recent.Count);
    }

    [Fact]
    public async Task GetRecentAsync_PreservesAllFieldValues()
    {
        var parameters = new Dictionary<string, string> { ["title"] = "buy milk", ["priority"] = "high" };
        var recordId   = Guid.NewGuid();
        var timestamp  = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var record = new InterpreterTrainingRecord
                     {
                             Id                    = recordId
                           , UserInput             = "  add high priority task buy milk  "
                           , NormalizedInput        = "add high priority task buy milk"
                           , ModelOutput            = """{"actionName":"AddTask"}"""
                           , FinalResolvedAction    = "AddTask"
                           , Parameters             = parameters
                           , RequiredClarification  = false
                           , ClarificationCount     = 0
                           , ExecutionSucceeded     = true
                           , FailureType            = "None"
                           , ModelVersion           = "phi3:mini"
                           , PromptVersion          = "system.txt"
                           , LatencyMs              = 312.5
                           , TimestampUtc           = timestamp
                     };

        await _store.SaveAsync(record);

        var recent = await _store.GetRecentAsync(1);
        var loaded = recent[0];

        Assert.Equal(recordId,    loaded.Id);
        Assert.Equal("  add high priority task buy milk  ", loaded.UserInput);
        Assert.Equal("add high priority task buy milk",     loaded.NormalizedInput);
        Assert.Equal("AddTask",   loaded.FinalResolvedAction);
        Assert.Equal("buy milk",  loaded.Parameters["title"]);
        Assert.Equal("high",      loaded.Parameters["priority"]);
        Assert.False(loaded.RequiredClarification);
        Assert.True(loaded.ExecutionSucceeded);
        Assert.Equal("None",      loaded.FailureType);
        Assert.Equal("phi3:mini", loaded.ModelVersion);
        Assert.Equal(312.5,       loaded.LatencyMs);
        Assert.Equal(timestamp,   loaded.TimestampUtc);
    }

    // -----------------------------------------------------------------------
    // GetForExportAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetForExportAsync_ReturnsAllRecords_WhenSucceededOnlyIsFalse()
    {
        await _store.SaveAsync(new InterpreterTrainingRecord { UserInput = "succeeded", ExecutionSucceeded = true,  TimestampUtc = DateTime.UtcNow });
        await _store.SaveAsync(new InterpreterTrainingRecord { UserInput = "failed",    ExecutionSucceeded = false, TimestampUtc = DateTime.UtcNow.AddSeconds(1) });

        var result = await _store.GetForExportAsync(100, succeededOnly: false);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetForExportAsync_ReturnsOnlySucceeded_WhenSucceededOnlyIsTrue()
    {
        await _store.SaveAsync(new InterpreterTrainingRecord { UserInput = "succeeded", ExecutionSucceeded = true,  TimestampUtc = DateTime.UtcNow });
        await _store.SaveAsync(new InterpreterTrainingRecord { UserInput = "failed",    ExecutionSucceeded = false, TimestampUtc = DateTime.UtcNow.AddSeconds(1) });

        var result = await _store.GetForExportAsync(100, succeededOnly: true);

        Assert.Single(result);
        Assert.True(result[0].ExecutionSucceeded);
    }

    [Fact]
    public async Task GetForExportAsync_RespectsLimitParameter()
    {
        for (var i = 0; i < 10; i++)
        {
            await _store.SaveAsync(new InterpreterTrainingRecord
                                   {
                                           UserInput    = $"input {i}"
                                         , TimestampUtc = DateTime.UtcNow.AddSeconds(i)
                                   });
        }

        var result = await _store.GetForExportAsync(3, succeededOnly: false);

        Assert.Equal(3, result.Count);
    }

    // -----------------------------------------------------------------------
    // GetStatsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatsAsync_ReturnsZeroMetrics_WhenEmpty()
    {
        var stats = await _store.GetStatsAsync();

        Assert.Equal(0,   stats.TotalRecords);
        Assert.Equal(0,   stats.SucceededCount);
        Assert.Equal(0.0, stats.SuccessRate);
        Assert.Equal(0.0, stats.AvgLatencyMs);
        Assert.Empty(stats.DailyCounts);
        Assert.Empty(stats.ActionCounts);
    }

    [Fact]
    public async Task GetStatsAsync_ComputesSuccessRate_FromSucceededAndTotal()
    {
        for (var i = 0; i < 3; i++)
        {
            await _store.SaveAsync(new InterpreterTrainingRecord
                                   {
                                           UserInput          = $"pass {i}"
                                         , ExecutionSucceeded = true
                                         , TimestampUtc       = DateTime.UtcNow.AddSeconds(i)
                                   });
        }
        await _store.SaveAsync(new InterpreterTrainingRecord
                               {
                                       UserInput          = "fail"
                                     , ExecutionSucceeded = false
                                     , TimestampUtc       = DateTime.UtcNow.AddSeconds(10)
                               });

        var stats = await _store.GetStatsAsync();

        Assert.Equal(4,    stats.TotalRecords);
        Assert.Equal(3,    stats.SucceededCount);
        Assert.Equal(0.75, stats.SuccessRate);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsActionDistribution_SortedByDescendingCount()
    {
        for (var i = 0; i < 5; i++)
        {
            await _store.SaveAsync(new InterpreterTrainingRecord
                                   {
                                           UserInput           = $"add {i}"
                                         , FinalResolvedAction = "AddTask"
                                         , TimestampUtc        = DateTime.UtcNow.AddSeconds(i)
                                   });
        }
        await _store.SaveAsync(new InterpreterTrainingRecord
                               {
                                       UserInput           = "journal"
                                     , FinalResolvedAction = "CreateJournalEntry"
                                     , TimestampUtc        = DateTime.UtcNow.AddSeconds(10)
                               });

        var stats = await _store.GetStatsAsync();

        Assert.Equal(2,                    stats.ActionCounts.Count);
        Assert.Equal("AddTask",            stats.ActionCounts[0].Action);
        Assert.Equal(5,                    stats.ActionCounts[0].RecordCount);
        Assert.Equal("CreateJournalEntry", stats.ActionCounts[1].Action);
        Assert.Equal(1,                    stats.ActionCounts[1].RecordCount);
    }

    [Fact]
    public async Task GetStatsAsync_ComputesAverageLatency()
    {
        await _store.SaveAsync(new InterpreterTrainingRecord { UserInput = "fast", LatencyMs = 100.0, TimestampUtc = DateTime.UtcNow             });
        await _store.SaveAsync(new InterpreterTrainingRecord { UserInput = "slow", LatencyMs = 300.0, TimestampUtc = DateTime.UtcNow.AddSeconds(1) });

        var stats = await _store.GetStatsAsync();

        Assert.Equal(200.0, stats.AvgLatencyMs);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsDailyCounts_GroupedByDay()
    {
        for (var i = 0; i < 3; i++)
        {
            await _store.SaveAsync(new InterpreterTrainingRecord
                                   {
                                           UserInput    = $"record {i}"
                                         , TimestampUtc = DateTime.UtcNow.AddSeconds(i)
                                   });
        }

        var stats = await _store.GetStatsAsync();

        Assert.Single(stats.DailyCounts);
        Assert.Equal(3, stats.DailyCounts[0].RecordCount);
    }
}
