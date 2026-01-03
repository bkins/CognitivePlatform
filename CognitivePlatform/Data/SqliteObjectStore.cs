using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using CognitivePlatform.Api.Avails.Extensions;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Data;

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
                Id           TEXT PRIMARY KEY,
                Type         TEXT NOT NULL,
                PartitionKey TEXT NULL,
                Json         TEXT NOT NULL,
                Mood         TEXT NULL,
                MoodScore    INTEGER NULL,
                MoodLevel    INTEGER NULL,
                MediaPaths   TEXT NULL,
                CreatedUtc   TEXT NOT NULL,
                UpdatedUtc   TEXT NOT NULL,
                DeletedUtc   TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Objects_Type_Partition_Deleted
                ON Objects(Type, PartitionKey, DeletedUtc);
            """;

        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------------
    // IObjectStore implementation
    // ---------------------------------------------------------------------
    public string Save<T> (T      value
                         , string? partitionKey = null
                         , string? id           = null)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;
        var objectId = ResolveAndApplyId(value, id);

        var nowString = DateTimeOffset
                       .UtcNow
                       .ToString("O");

        var json = JsonSerializer.Serialize(value
                                           , _jsonOptions);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
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
        if (string.IsNullOrWhiteSpace(id))
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
        var         list = new List<T>();

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
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Value cannot be null or whitespace."
                                      , nameof(id));

        var type     = typeof(T);
        var typeName = type.FullName ?? type.Name;
        var now      = DateTimeOffset
                      .UtcNow
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
    // Helpers
    // ---------------------------------------------------------------------
    private static string ResolveAndApplyId<T> (T      value
                                              , string? explicitId)
    {
        var type       = typeof(T);
        var idProperty = GetIdProperty(type);

        var effectiveId = explicitId;

        if (effectiveId.DoesNotHaveValueOrIsNullOrEmpty()
         && idProperty is not null)
        {
            var current = idProperty.GetValue(value) as string;
            
            if (current.HasValue()) effectiveId = current;
        }

        if (string.IsNullOrWhiteSpace(effectiveId))
            effectiveId = Guid
                         .NewGuid()
                         .ToString("N");

        if (idProperty is not null)
            idProperty.SetValue(value
                               , effectiveId);

        return effectiveId;
    }

    private static PropertyInfo? GetIdProperty(Type type)
    {
        return IdPropertyCache.GetOrAdd(type
                                      , t => t.GetProperty("Id"
                                                          , BindingFlags.Public
                                                          | BindingFlags.Instance));
    }
}
