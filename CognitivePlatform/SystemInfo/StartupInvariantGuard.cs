using System.Text.Json;
using CognitivePlatform.Api.Models.SystemInfo;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.System;

public sealed class StartupInvariantGuard
{
    private readonly ILogger<StartupInvariantGuard> _log;
    private readonly IHostEnvironment               _env;
    private readonly SystemService                  _system;

    public StartupInvariantGuard(ILogger<StartupInvariantGuard> log,
                                 IHostEnvironment               env,
                                 SystemService                  system)
    {
        _log    = log;
        _env    = env;
        _system = system;
    }

    public void Enforce()
    {
        var envName = (_env.EnvironmentName ?? string.Empty)
                      .Trim()
                      .ToUpperInvariant();

        if (envName != "PROD")
            return;

        var info = _system.GetEnvironment();

        EnsureDirectory(info.DataRoot);
        EnsureDatabase(info.DatabasePath);
        EnsureSentinel(info.DatabasePath);
    }

    private void EnsureDirectory (string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex
                           , "Failed to ensure PROD data directory: {Path}"
                           , path);
            throw;
        }
    }

    private void EnsureDatabase (string dbPath)
    {
        try
        {
            if (File.Exists(dbPath).Not())
            {
                // Minimal create — open/close is enough for SQLite
                using var _ = new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={dbPath}");
            }
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex
                           , "Failed to create or open PROD database: {DbPath}"
                           , dbPath);
            throw;
        }
    }

    private void EnsureSentinel (string dbPath)
    {
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS system_metadata (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );
                    """;
            cmd.ExecuteNonQuery();

            // Read sentinel
            cmd.CommandText = "SELECT value FROM system_metadata WHERE key = 'prod_sentinel';";
            var existing = cmd.ExecuteScalar() as string;

            if (string.IsNullOrWhiteSpace(existing))
            {
                var sentinel = new ProdSentinel
                               {
                                       CreatedAtUtc = DateTime.UtcNow
                               };

                var json = JsonSerializer.Serialize(sentinel);

                cmd.CommandText = "INSERT INTO system_metadata (key, value) VALUES ('prod_sentinel', @json);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@json"
                                          , json);
                cmd.ExecuteNonQuery();

                _log.LogInformation("PROD sentinel created.");
                return;
            }

            // Validate
            var parsed = JsonSerializer.Deserialize<ProdSentinel>(existing);
            if (string.Equals(parsed?.Environment
                             , "PROD"
                             , StringComparison.OrdinalIgnoreCase)
                      .Not())
            {
                _log.LogCritical("Invalid PROD sentinel. Expected Environment=PROD, found {Env}."
                               , parsed?.Environment);
                throw new InvalidOperationException("Invalid PROD sentinel.");
            }
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex
                           , "Failed PROD sentinel validation.");
            throw;
        }
    }
}
