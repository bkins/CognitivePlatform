using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Data;

/// <summary>
/// ObjectStore is infrastructure.
/// Domain Services own meaning.
/// KnowledgeService coordinates meaning across domains.
/// </summary>
public class SqliteObjectStore : IObjectStore
{
    private readonly string                _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> IdPropertyCache = new();

    public SqliteObjectStore (string                connectionString
                            , JsonSerializerOptions? jsonOptions = null)
    {
        _connectionString = connectionString;
        _jsonOptions      = jsonOptions ?? new JsonSerializerOptions
                                            {
                                                WriteIndented        = false
                                              , PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                                              , Converters           = { new JsonStringEnumConverter() }
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
            CREATE TABLE IF NOT EXISTS Objects
            (
                Id           TEXT PRIMARY KEY
              , Type         TEXT NOT NULL
              , PartitionKey TEXT NULL
              , Json         TEXT NOT NULL
              , Mood         TEXT NULL
              , MoodScore    INTEGER NULL
              , MoodLevel    INTEGER NULL
              , MediaPaths   TEXT NULL
              , CreatedUtc   TEXT NOT NULL
              , UpdatedUtc   TEXT NOT NULL
              , DeletedUtc   TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Objects_Type_Partition_Deleted
                ON Objects(Type, PartitionKey, DeletedUtc);
            """;

        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------------
    // IObjectStore implementation
    // ---------------------------------------------------------------------
    public async Task<string> Save<T> (T       value
                               , string? partitionKey = null
                               , string? id           = null)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;
        var objectId = ResolveAndApplyId(value, id);

        var nowString = DateTimeOffset.UtcNow
                                      .ToString("O");

        var json = JsonSerializer.Serialize(value
                                           , _jsonOptions);

        await using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Objects (Id, Type, PartitionKey, Json, CreatedUtc, UpdatedUtc, DeletedUtc)
            VALUES ($id, $type, $partitionKey, $json, $now, $now, NULL)
            ON CONFLICT(Id) DO UPDATE SET
                Json         = excluded.Json,
                UpdatedUtc   = excluded.UpdatedUtc,
                PartitionKey = excluded.PartitionKey,
                DeletedUtc   = NULL;
            """;

        command.Parameters.AddWithValue("$id"
                                      , objectId);
        command.Parameters.AddWithValue("$type"
                                      , typeName);
        command.Parameters.AddWithValue("$partitionKey"
                                      , (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$json"
                                      , json);
        command.Parameters.AddWithValue("$now"
                                      , nowString);

        command.ExecuteNonQuery();

        return objectId;
    }

    public T? Get<T> (string id
                    , string? partitionKey = null)
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace."
                                      , nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Json
            FROM Objects
            WHERE Id = $id
              AND Type = $type
              AND DeletedUtc IS NULL
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$id"
                                      , id);
        command.Parameters.AddWithValue("$type"
                                      , typeName);
        command.Parameters.AddWithValue("$partitionKey"
                                      , (object?)partitionKey ?? DBNull.Value);

        using var reader = command.ExecuteReader();

        if (reader.Read().Not())
            return default;

        var json = reader.GetString(0);

        return JsonSerializer.Deserialize<T>(json
                                           , _jsonOptions);
    }
    public T? GetDeleted<T> (string  id
                    , string? partitionKey = null)
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace."
                                      , nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
                """
                SELECT Json
                FROM Objects
                WHERE Id = $id
                  AND Type = $type
                  AND PartitionKey IS $partitionKey;
                """;

        command.Parameters.AddWithValue("$id"
                                      , id);
        command.Parameters.AddWithValue("$type"
                                      , typeName);
        command.Parameters.AddWithValue("$partitionKey"
                                      , (object?)partitionKey ?? DBNull.Value);

        using var reader = command.ExecuteReader();

        if (reader.Read().Not())
            return default;

        var json = reader.GetString(0);

        return JsonSerializer.Deserialize<T>(json
                                           , _jsonOptions);
    }
    
    public IReadOnlyList<T> List<T> (string?         partitionKey = null
                                   , DateTimeOffset? fromUtc      = null
                                   , DateTimeOffset? toUtc        = null)
    {
        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Json
            FROM Objects
            WHERE Type = $type
              AND DeletedUtc IS NULL
              AND PartitionKey IS $partitionKey
              AND ($fromUtc IS NULL OR CreatedUtc >= $fromUtc)
              AND ($toUtc   IS NULL OR CreatedUtc <= $toUtc)
            ORDER BY CreatedUtc;
            """;

        command.Parameters.AddWithValue("$type"
                                      , typeName);
        command.Parameters.AddWithValue("$partitionKey"
                                      , (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$fromUtc"
                                      , fromUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$toUtc"
                                      , toUtc?.ToString("O") ?? (object)DBNull.Value);

        using var reader = command.ExecuteReader();

        var list = new List<T>();

        while (reader.Read())
        {
            var json  = reader.GetString(0);
            var value = JsonSerializer.Deserialize<T>(json
                                                    , _jsonOptions);

            if (value is not null)
                list.Add(value);
        }

        return list;
    }

    public bool SoftDelete<T> (string id
                             , string? partitionKey = null)
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace."
                                      , nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;
        var now      = DateTimeOffset.UtcNow
                                     .ToString("O");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Objects
            SET DeletedUtc = $deletedUtc
            WHERE Id = $id
              AND Type = $type
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$id",           id);
        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$deletedUtc",   now);

        command.ExecuteNonQuery();

        return true;
    }

    // ---------------------------------------------------------------------
    // Async variants — promoted from sync on hot paths (IObjectStore)
    // ---------------------------------------------------------------------

    public async Task<T?> GetAsync<T>( string            id
                                     , string?           partitionKey      = null
                                     , CancellationToken cancellationToken = default )
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Json
            FROM Objects
            WHERE Id = $id
              AND Type = $type
              AND DeletedUtc IS NULL
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$id",           id);
        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return default;

        var json = reader.GetString(0);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    public async Task<IReadOnlyList<T>> ListAsync<T>( string?           partitionKey      = null
                                                     , DateTimeOffset?   fromUtc           = null
                                                     , DateTimeOffset?   toUtc             = null
                                                     , CancellationToken cancellationToken = default )
    {
        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Json
            FROM Objects
            WHERE Type = $type
              AND DeletedUtc IS NULL
              AND PartitionKey IS $partitionKey
              AND ($fromUtc IS NULL OR CreatedUtc >= $fromUtc)
              AND ($toUtc   IS NULL OR CreatedUtc <= $toUtc)
            ORDER BY CreatedUtc;
            """;

        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$fromUtc",      fromUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$toUtc",        toUtc?.ToString("O")   ?? (object)DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var json  = reader.GetString(0);
            var value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            if (value is not null)
                list.Add(value);
        }

        return list;
    }

    public async Task<bool> SoftDeleteAsync<T>( string            id
                                               , string?           partitionKey      = null
                                               , CancellationToken cancellationToken = default )
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;
        var now      = DateTimeOffset.UtcNow.ToString("O");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Objects
            SET DeletedUtc = $deletedUtc
            WHERE Id = $id
              AND Type = $type
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$id",           id);
        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$deletedUtc",   now);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    // ---------------------------------------------------------------------
    // Admin operations — not on IObjectStore; admin surface only
    // ---------------------------------------------------------------------

    /// <summary>
    /// Permanently removes a record from the store. Admin use only.
    /// This bypasses the soft-delete invariant by design.
    /// </summary>
    public bool HardDelete<T> (string  id
                              , string? partitionKey = null)
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM Objects
            WHERE Id            = $id
              AND Type          = $type
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$id",           id);
        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);

        var rowsAffected = command.ExecuteNonQuery();

        return rowsAffected > 0;
    }

    /// <summary>
    /// Same as List&lt;T&gt; but includes soft-deleted records. Admin use only.
    /// </summary>
    public IReadOnlyList<T> ListIncludingDeleted<T> (string?         partitionKey = null
                                                    , DateTimeOffset? fromUtc      = null
                                                    , DateTimeOffset? toUtc        = null)
    {
        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Json
            FROM Objects
            WHERE Type = $type
              AND PartitionKey IS $partitionKey
              AND ($fromUtc IS NULL OR CreatedUtc >= $fromUtc)
              AND ($toUtc   IS NULL OR CreatedUtc <= $toUtc)
            ORDER BY CreatedUtc;
            """;

        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$fromUtc",      fromUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$toUtc",        toUtc?.ToString("O") ?? (object)DBNull.Value);

        using var reader = command.ExecuteReader();

        var list = new List<T>();

        while (reader.Read())
        {
            var json  = reader.GetString(0);
            var value = JsonSerializer.Deserialize<T>(json
                                                    , _jsonOptions);

            if (value is not null)
                list.Add(value);
        }

        return list;
    }

    /// <summary>
    /// Hard-deletes all rows of type <typeparamref name="T"/> whose
    /// <c>CreatedUtc</c> is older than <paramref name="olderThan"/> from now.
    /// Intended for maintenance-only eviction of ephemeral records (e.g. idempotency cache).
    /// Returns the number of rows deleted.
    /// </summary>
    public int DeleteOlderThan<T>(TimeSpan olderThan, string? partitionKey = null)
    {
        var type      = typeof(T);
        var typeName  = type.FullName ?? type.Name;
        var cutoffUtc = (DateTimeOffset.UtcNow - olderThan).ToString("O");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM Objects
            WHERE Type = $type
              AND CreatedUtc < $cutoff
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$cutoff",       cutoffUtc);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);

        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns row counts grouped by type — total and soft-deleted. Admin use only.
    /// </summary>
    public IReadOnlyList<ObjectTypeCount> GetObjectTypeCounts()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT   Type
                   , COUNT(*)                                                 AS Total
                   , SUM(CASE WHEN DeletedUtc IS NOT NULL THEN 1 ELSE 0 END) AS SoftDeleted
            FROM Objects
            GROUP BY Type
            ORDER BY Type;
            """;

        using var reader = command.ExecuteReader();
        var         list = new List<ObjectTypeCount>();

        while (reader.Read())
        {
            list.Add(new ObjectTypeCount
                     {
                             TypeName    = reader.GetString(0)
                           , Total       = reader.GetInt32(1)
                           , SoftDeleted = reader.GetInt32(2)
                     });
        }

        return list;
    }

    /// <summary>
    /// Clears DeletedUtc, restoring the record as an active object. Admin use only.
    /// </summary>
    public bool Undelete<T> (string  id
                            , string? partitionKey = null)
    {
        if (id.HasNoValue())
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Objects
            SET DeletedUtc = NULL
            WHERE Id   = $id
              AND Type = $type
              AND PartitionKey IS $partitionKey;
            """;

        command.Parameters.AddWithValue("$id",           id);
        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);

        var rowsAffected = command.ExecuteNonQuery();

        return rowsAffected > 0;
    }

    /// <summary>
    /// Sets PartitionKey = NULL for any rows of the given type where PartitionKey = Id.
    /// Repairs records written by old pre-workspace code that stored the object's own Id
    /// as its PartitionKey instead of leaving it NULL (the personal-workspace sentinel).
    /// When jsonIdField is provided, a second pass also nullifies rows where PartitionKey
    /// equals the value of that JSON field — needed for types whose key property is not
    /// named "Id" (e.g. JournalRevision whose key is revisionId in JSON).
    /// Idempotent. Admin use only.
    /// </summary>
    /// <summary>
    /// Returns the IDs of every repaired row so the caller can show the user which records changed.
    /// Idempotent — re-running after all rows are clean returns an empty list.
    /// </summary>
    public IReadOnlyList<string> NullifyOrphanedPartitionKeys(string typeName, string? jsonIdField = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var repairedIds = new List<string>();

        using var tx = connection.BeginTransaction();

        // Pass 1: rows where PartitionKey mistakenly equals the row's own Id
        using (var sel = connection.CreateCommand())
        {
            sel.Transaction  = tx;
            sel.CommandText  =
                """
                SELECT Id FROM Objects
                WHERE  Type         = $type
                  AND  PartitionKey = Id;
                """;
            sel.Parameters.AddWithValue("$type", typeName);

            using var reader = sel.ExecuteReader();
            while (reader.Read())
                repairedIds.Add(reader.GetString(0));
        }

        if (repairedIds.Count > 0)
        {
            using var upd = connection.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText =
                """
                UPDATE Objects
                SET    PartitionKey = NULL
                WHERE  Type         = $type
                  AND  PartitionKey = Id;
                """;
            upd.Parameters.AddWithValue("$type", typeName);
            upd.ExecuteNonQuery();
        }

        // Pass 2 (optional): rows where PartitionKey matches a JSON field value instead of the row Id
        if (jsonIdField is not null)
        {
            var extraIds = new List<string>();

            using var sel2 = connection.CreateCommand();
            sel2.Transaction = tx;
            sel2.CommandText =
                """
                SELECT Id FROM Objects
                WHERE  Type         = $type
                  AND  PartitionKey != Id
                  AND  PartitionKey  = json_extract(Json, $jsonPath);
                """;
            sel2.Parameters.AddWithValue("$type",     typeName);
            sel2.Parameters.AddWithValue("$jsonPath", $"$.{jsonIdField}");

            using var reader2 = sel2.ExecuteReader();
            while (reader2.Read())
                extraIds.Add(reader2.GetString(0));

            if (extraIds.Count > 0)
            {
                using var upd2 = connection.CreateCommand();
                upd2.Transaction = tx;
                upd2.CommandText =
                    """
                    UPDATE Objects
                    SET    PartitionKey = NULL
                    WHERE  Type         = $type
                      AND  PartitionKey != Id
                      AND  PartitionKey  = json_extract(Json, $jsonPath);
                    """;
                upd2.Parameters.AddWithValue("$type",     typeName);
                upd2.Parameters.AddWithValue("$jsonPath", $"$.{jsonIdField}");
                upd2.ExecuteNonQuery();

                repairedIds.AddRange(extraIds);
            }
        }

        tx.Commit();
        return repairedIds;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------
    private static string ResolveAndApplyId<T> (T      value
                                              , string? explicitId)
    {
        var type       = typeof(T);
        var idProperty = GetIdProperty(type);

        var effectiveId = explicitId;

        if (effectiveId?.HasNoValue() ?? true
         && idProperty is not null)
        {
            var currentObj = idProperty.GetValue(value);
            if (currentObj is string str && str.HasValue())
            {
                effectiveId = str;
            }
            else if (currentObj is Guid guid && guid != Guid.Empty)
            {
                effectiveId = guid.ToString();
            }
        }

        if (effectiveId?.HasNoValue() ?? true)
            effectiveId = Guid.NewGuid()
                              .ToString("N");

        if (idProperty is not null && idProperty.CanWrite)
        {
            if (idProperty.PropertyType == typeof(string))
            {
                idProperty.SetValue(value, effectiveId);
            }
            else if (idProperty.PropertyType == typeof(Guid) && Guid.TryParse(effectiveId, out var parsedGuid))
            {
                idProperty.SetValue(value, parsedGuid);
            }
        }

        return effectiveId;
    }

    private static PropertyInfo? GetIdProperty(Type type)
    {
        return IdPropertyCache.GetOrAdd(type
                                      , theType => theType.GetProperty("Id"
                                                                     , BindingFlags.Public
                                                                     | BindingFlags.Instance));
    }
}
