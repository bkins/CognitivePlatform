using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Integrations.CrossApp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CognitivePlatform.Tests;

public class WatchListBridgeTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    public WatchListBridgeTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"CP_Bridge_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath     = Path.Combine(_tempFolder, "watchlist.db");

        InitializeTestDb(_dbPath);
    }

    private static void InitializeTestDb(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS WatchItems (
                Id TEXT PRIMARY KEY,
                Title TEXT,
                StreamingService TEXT,
                Category TEXT,
                IsWatched INTEGER,
                IsLiked INTEGER,
                IsDeleted INTEGER,
                DeepLinkUri TEXT,
                LastUpdated TEXT,
                Type TEXT,
                PreviousCategory TEXT,
                ApiSource TEXT,
                Overview TEXT,
                PosterUrl TEXT,
                AvailableStreamingServices TEXT,
                AggregatedDataJson TEXT
            );";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task PingAsync_WhenDisabled_ReturnsFalse()
    {
        var settings = Options.Create(new CrossAppSettings
                                      {
                                          WatchList = new WatchListSettings
                                                      {
                                                          Enabled = false
                                                        , DbPath  = _dbPath
                                                      }
                                      });
        var connector = new WatchListConnector(settings, NullLogger<WatchListConnector>.Instance);

        var result = await connector.PingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task PingAsync_WhenEnabledAndDbExists_ReturnsTrue()
    {
        var settings = Options.Create(new CrossAppSettings
                                      {
                                          WatchList = new WatchListSettings
                                                      {
                                                          Enabled = true
                                                        , DbPath  = _dbPath
                                                      }
                                      });
        var connector = new WatchListConnector(settings, NullLogger<WatchListConnector>.Instance);

        var result = await connector.PingAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task CrossAppActions_AddWatchItemAndListWatchItems_Succeeds()
    {
        var settings = Options.Create(new CrossAppSettings
                                      {
                                          WatchList = new WatchListSettings
                                                      {
                                                          Enabled = true
                                                        , DbPath  = _dbPath
                                                      }
                                      });
        var connector = new WatchListConnector(settings, NullLogger<WatchListConnector>.Instance);
        var registry  = new ExternalAppConnectorRegistry(new[] { connector });
        var actions   = new CrossAppActions(registry);

        var addResult = await actions.AddWatchItem("Inception", "Netflix", "Currently Watching", "Movie", CancellationToken.None);

        Assert.True(addResult.Success);
        Assert.Contains("Inception", addResult.Message);

        var listResult = await actions.ListWatchItems(10, CancellationToken.None);

        Assert.True(listResult.Success);
        Assert.Contains("Inception", listResult.Message);
        Assert.Contains("Netflix", listResult.Message);
    }

    [Fact]
    public async Task CrossAppActions_CompleteWatchItem_MarksItemAsWatched()
    {
        var settings = Options.Create(new CrossAppSettings
                                      {
                                          WatchList = new WatchListSettings
                                                      {
                                                          Enabled = true
                                                        , DbPath  = _dbPath
                                                      }
                                      });
        var connector = new WatchListConnector(settings, NullLogger<WatchListConnector>.Instance);
        var registry  = new ExternalAppConnectorRegistry(new[] { connector });
        var actions   = new CrossAppActions(registry);

        await actions.AddWatchItem("Interstellar", "Prime Video", "Currently Watching", "Movie", CancellationToken.None);
        var completeResult = await actions.CompleteWatchItem("Interstellar", CancellationToken.None);

        Assert.True(completeResult.Success);
        Assert.Contains("Successfully completed", completeResult.Message);

        var listResult = await actions.ListWatchItems(10, CancellationToken.None);
        Assert.Contains("Watched", listResult.Message);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, true);
            }
        }
        catch
        {
            // Safe ignore
        }
    }
}
