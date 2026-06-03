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
            records.Add(new InterpreterTrainingRecord
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
                        });
        }

        return records;
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
}
