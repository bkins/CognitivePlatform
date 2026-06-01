using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
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
              AND ($partitionKey IS NULL OR PartitionKey = $partitionKey);
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
                  AND Type = $type;
                """;

        command.Parameters.AddWithValue("$id"
                                      , id);
        command.Parameters.AddWithValue("$type"
                                      , typeName);

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
              AND ($partitionKey IS NULL OR PartitionKey = $partitionKey)
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
              AND ($partitionKey IS NULL OR PartitionKey = $partitionKey);
            """;

        command.Parameters.AddWithValue("$id",           id);
        command.Parameters.AddWithValue("$type",         typeName);
        command.Parameters.AddWithValue("$partitionKey", (object?)partitionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$deletedUtc",   now);

        command.ExecuteNonQuery();
        
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
              AND ($partitionKey IS NULL OR PartitionKey = $partitionKey);
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
              AND ($partitionKey IS NULL OR PartitionKey = $partitionKey)
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
              AND ($partitionKey IS NULL OR PartitionKey = $partitionKey);
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
    /// Idempotent. Admin use only.
    /// </summary>
    public int NullifyOrphanedPartitionKeys(string typeName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Objects
            SET    PartitionKey = NULL
            WHERE  Type         = $type
              AND  PartitionKey = Id;
            """;

        command.Parameters.AddWithValue("$type", typeName);

        return command.ExecuteNonQuery();
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
            var current = idProperty?.GetValue(value) as string;
            
            if (current.HasValue()) effectiveId = current;
        }

        if (effectiveId?.HasNoValue() ?? true)
            effectiveId = Guid.NewGuid()
                              .ToString("N");

        idProperty?.SetValue(value
                           , effectiveId);

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
