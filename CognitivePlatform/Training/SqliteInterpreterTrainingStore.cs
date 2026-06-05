using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Training;

public class SqliteInterpreterTrainingStore : IInterpreterTrainingStore
{
    private readonly string                _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteInterpreterTrainingStore(string connectionString)
    {
        _connectionString = connectionString;
        _jsonOptions      = new JsonSerializerOptions
                            {
                                WriteIndented        = false
                              , PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                            };

        EnsureSchema();
    }

    // ---------------------------------------------------------------------
    // Schema bootstrap
    // ---------------------------------------------------------------------
    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS InterpreterTrainingRecords
            (
                Id                    TEXT PRIMARY KEY
              , UserInput             TEXT NOT NULL
              , NormalizedInput       TEXT NOT NULL
              , ModelOutput           TEXT NOT NULL
              , FinalResolvedAction   TEXT NOT NULL
              , Parameters            TEXT NOT NULL
              , RequiredClarification INTEGER NOT NULL
              , ClarificationCount    INTEGER NOT NULL
              , ExecutionSucceeded    INTEGER NOT NULL
              , FailureType           TEXT NOT NULL
              , ModelVersion          TEXT NOT NULL
              , PromptVersion         TEXT NOT NULL
              , LatencyMs             REAL NOT NULL
              , TimestampUtc          TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_TrainingRecords_Timestamp
                ON InterpreterTrainingRecords(TimestampUtc DESC);
            """;

        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------------
    // IInterpreterTrainingStore
    // ---------------------------------------------------------------------
    public async Task SaveAsync(InterpreterTrainingRecord record, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO InterpreterTrainingRecords
                (Id, UserInput, NormalizedInput, ModelOutput, FinalResolvedAction,
                 Parameters, RequiredClarification, ClarificationCount, ExecutionSucceeded,
                 FailureType, ModelVersion, PromptVersion, LatencyMs, TimestampUtc)
            VALUES
                ($id, $userInput, $normalizedInput, $modelOutput, $finalResolvedAction,
                 $parameters, $requiredClarification, $clarificationCount, $executionSucceeded,
                 $failureType, $modelVersion, $promptVersion, $latencyMs, $timestampUtc)
            ON CONFLICT(Id) DO NOTHING;
            """;

        command.Parameters.AddWithValue("$id",                    record.Id.ToString());
        command.Parameters.AddWithValue("$userInput",             record.UserInput);
        command.Parameters.AddWithValue("$normalizedInput",       record.NormalizedInput);
        command.Parameters.AddWithValue("$modelOutput",           record.ModelOutput);
        command.Parameters.AddWithValue("$finalResolvedAction",   record.FinalResolvedAction);
        command.Parameters.AddWithValue("$parameters",            JsonSerializer.Serialize(record.Parameters, _jsonOptions));
        command.Parameters.AddWithValue("$requiredClarification", record.RequiredClarification ? 1 : 0);
        command.Parameters.AddWithValue("$clarificationCount",    record.ClarificationCount);
        command.Parameters.AddWithValue("$executionSucceeded",    record.ExecutionSucceeded ? 1 : 0);
        command.Parameters.AddWithValue("$failureType",           record.FailureType);
        command.Parameters.AddWithValue("$modelVersion",          record.ModelVersion);
        command.Parameters.AddWithValue("$promptVersion",         record.PromptVersion);
        command.Parameters.AddWithValue("$latencyMs",             record.LatencyMs);
        command.Parameters.AddWithValue("$timestampUtc",          record.TimestampUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IList<InterpreterTrainingRecord>> GetRecentAsync(int count = 100, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, UserInput, NormalizedInput, ModelOutput, FinalResolvedAction,
                   Parameters, RequiredClarification, ClarificationCount, ExecutionSucceeded,
                   FailureType, ModelVersion, PromptVersion, LatencyMs, TimestampUtc
            FROM InterpreterTrainingRecords
            ORDER BY TimestampUtc DESC
            LIMIT $count;
            """;

        command.Parameters.AddWithValue("$count", count);

        await using var reader  = await command.ExecuteReaderAsync(ct);
        var             records = new List<InterpreterTrainingRecord>();

        while (await reader.ReadAsync(ct))
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    public async Task<IList<InterpreterTrainingRecord>> GetForExportAsync(int limit, bool succeededOnly, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, UserInput, NormalizedInput, ModelOutput, FinalResolvedAction,
                   Parameters, RequiredClarification, ClarificationCount, ExecutionSucceeded,
                   FailureType, ModelVersion, PromptVersion, LatencyMs, TimestampUtc
            FROM InterpreterTrainingRecords
            WHERE ($succeededOnly = 0 OR ExecutionSucceeded = 1)
            ORDER BY TimestampUtc DESC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$succeededOnly", succeededOnly ? 1 : 0);
        command.Parameters.AddWithValue("$limit",         limit);

        await using var reader  = await command.ExecuteReaderAsync(ct);
        var             records = new List<InterpreterTrainingRecord>();

        while (await reader.ReadAsync(ct))
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    public async Task<TrainingCorpusStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var stats = new TrainingCorpusStats();

        // Aggregate totals
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT COUNT(*)                AS TotalRecords
                     , COALESCE(SUM(ExecutionSucceeded), 0) AS SucceededCount
                     , COALESCE(AVG(LatencyMs), 0)         AS AvgLatencyMs
                FROM InterpreterTrainingRecords;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                stats.TotalRecords   = reader.GetInt32(0);
                stats.SucceededCount = reader.GetInt32(1);
                stats.AvgLatencyMs   = reader.GetDouble(2);
                stats.SuccessRate    = stats.TotalRecords > 0
                                       ? (double)stats.SucceededCount / stats.TotalRecords
                                       : 0;
            }
        }

        // Records per day
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT DATE(TimestampUtc) AS Day, COUNT(*) AS RecordCount
                FROM InterpreterTrainingRecords
                GROUP BY DATE(TimestampUtc)
                ORDER BY Day DESC;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                stats.DailyCounts.Add(new DailyRecordCount
                                      {
                                          Day         = reader.GetString(0)
                                        , RecordCount = reader.GetInt32(1)
                                      });
            }
        }

        // Action distribution
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT FinalResolvedAction, COUNT(*) AS RecordCount
                FROM InterpreterTrainingRecords
                GROUP BY FinalResolvedAction
                ORDER BY RecordCount DESC;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                stats.ActionCounts.Add(new ActionRecordCount
                                       {
                                           Action      = reader.GetString(0)
                                         , RecordCount = reader.GetInt32(1)
                                       });
            }
        }

        return stats;
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM InterpreterTrainingRecords;";

        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    private InterpreterTrainingRecord MapRecord(SqliteDataReader reader)
        => new InterpreterTrainingRecord
           {
               Id                    = Guid.Parse(reader.GetString(0))
             , UserInput             = reader.GetString(1)
             , NormalizedInput       = reader.GetString(2)
             , ModelOutput           = reader.GetString(3)
             , FinalResolvedAction   = reader.GetString(4)
             , Parameters            = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5)
                                                                                            , _jsonOptions) ?? []
             , RequiredClarification = reader.GetInt32(6) != 0
             , ClarificationCount    = reader.GetInt32(7)
             , ExecutionSucceeded    = reader.GetInt32(8) != 0
             , FailureType           = reader.GetString(9)
             , ModelVersion          = reader.GetString(10)
             , PromptVersion         = reader.GetString(11)
             , LatencyMs             = reader.GetDouble(12)
             , TimestampUtc          = DateTime.Parse(reader.GetString(13)
                                                     , null
                                                     , DateTimeStyles.RoundtripKind)
           };
}
