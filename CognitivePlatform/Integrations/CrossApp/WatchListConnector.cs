using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Integrations.CrossApp;

public sealed class WatchListConnector : IExternalAppConnector
{
    private readonly CrossAppSettings _settings;
    private readonly ILogger<WatchListConnector> _logger;

    public string AppName => "WatchList";

    public bool IsConfigured => _settings.WatchList.Enabled && _settings.WatchList.DbPath.HasValue();

    public WatchListConnector(IOptions<CrossAppSettings> settings, ILogger<WatchListConnector> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return Task.FromResult(false);

        try
        {
            var dbPath = _settings.WatchList.DbPath;
            if (!File.Exists(dbPath))
            {
                _logger.LogWarning($"WatchList database file not found at: {dbPath}");
                return Task.FromResult(false);
            }

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM WatchItems;";
            command.ExecuteScalar();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ping WatchList SQLite database.");
            return Task.FromResult(false);
        }
    }

    public async Task<object?> ExecuteActionAsync(string actionName, IDictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("WatchList connector is not enabled or configured.");
        }

        var dbPath = _settings.WatchList.DbPath;

        switch (actionName)
        {
            case "AddWatchItem":
                return await AddWatchItemAsync(dbPath, parameters, ct);
            case "ListWatchItems":
                return await ListWatchItemsAsync(dbPath, parameters, ct);
            case "CompleteWatchItem":
                return await CompleteWatchItemAsync(dbPath, parameters, ct);
            default:
                throw new NotSupportedException($"Action '{actionName}' is not supported by the WatchList connector.");
        }
    }

    private async Task<bool> AddWatchItemAsync(string dbPath, IDictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("title", out var titleObj) || titleObj is not string title || title.HasNoValue())
        {
            throw new ArgumentException("Parameter 'title' is required and must be a non-empty string.");
        }

        var streamingService = parameters.TryGetValue("streamingService", out var ss) ? ss.ToString() : string.Empty;
        var category = parameters.TryGetValue("category", out var cat) ? cat.ToString() : "Currently Watching";
        var type = parameters.TryGetValue("type", out var t) ? t.ToString() : "Movie";

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO WatchItems (Id, Title, StreamingService, Category, IsWatched, IsLiked, IsDeleted, DeepLinkUri, LastUpdated, Type, PreviousCategory, ApiSource, Overview, PosterUrl, AvailableStreamingServices, AggregatedDataJson)
            VALUES ($id, $title, $streamingService, $category, 0, 0, 0, '', $lastUpdated, $type, '', 'CP', '', '', '', '');";

        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$streamingService", streamingService ?? string.Empty);
        command.Parameters.AddWithValue("$category", category ?? string.Empty);
        command.Parameters.AddWithValue("$lastUpdated", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("$type", type ?? string.Empty);

        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    private async Task<List<Dictionary<string, object>>> ListWatchItemsAsync(string dbPath, IDictionary<string, object> parameters, CancellationToken ct)
    {
        var limit = parameters.TryGetValue("limit", out var limObj) && int.TryParse(limObj.ToString(), out var parsedLim) ? parsedLim : 20;

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, StreamingService, Category, IsWatched, Type FROM WatchItems WHERE IsDeleted = 0 ORDER BY LastUpdated DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<Dictionary<string, object>>();

        while (await reader.ReadAsync(ct))
        {
            items.Add(new Dictionary<string, object>
            {
                { "Id", reader.GetString(0) },
                { "Title", reader.GetString(1) },
                { "StreamingService", reader.IsDBNull(2) ? string.Empty : reader.GetString(2) },
                { "Category", reader.IsDBNull(3) ? string.Empty : reader.GetString(3) },
                { "IsWatched", reader.GetInt32(4) != 0 },
                { "Type", reader.IsDBNull(5) ? string.Empty : reader.GetString(5) }
            });
        }

        return items;
    }

    private async Task<bool> CompleteWatchItemAsync(string dbPath, IDictionary<string, object> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("title", out var titleObj) || titleObj is not string title || title.HasNoValue())
        {
            throw new ArgumentException("Parameter 'title' is required and must be a non-empty string.");
        }

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE WatchItems 
            SET IsWatched = 1, Category = 'Finished Watching', LastUpdated = $lastUpdated 
            WHERE Title LIKE $title AND IsDeleted = 0;";
        
        command.Parameters.AddWithValue("$title", $"%{title}%");
        command.Parameters.AddWithValue("$lastUpdated", DateTime.UtcNow.ToString("o"));

        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }
}
