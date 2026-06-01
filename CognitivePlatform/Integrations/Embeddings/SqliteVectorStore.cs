using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Integrations.Embeddings;

/// <summary>
/// IVectorStore backed by a "vectors" table in the platform's existing SQLite database.
/// Cosine similarity is computed in-process over all loaded embeddings — suitable for
/// corpora in the hundreds; revisit when the table grows into the tens of thousands.
/// </summary>
public sealed class SqliteVectorStore : IVectorStore
{
    private readonly string _connectionString;

    public SqliteVectorStore(string connectionString)
    {
        _connectionString = connectionString;
        EnsureSchema();
    }

    // -----------------------------------------------------------------------
    // IVectorStore
    // -----------------------------------------------------------------------

    public async Task SaveAsync(VectorEntry entry, CancellationToken ct = default)
    {
        var blob = MemoryMarshal.AsBytes(entry.Embedding.AsSpan()).ToArray();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO vectors (id, domain, referenceId, text, embedding, embeddedAt)
            VALUES ($id, $domain, $referenceId, $text, $embedding, $embeddedAt)
            """;
        command.Parameters.AddWithValue("$id",          entry.Id);
        command.Parameters.AddWithValue("$domain",      entry.Domain);
        command.Parameters.AddWithValue("$referenceId", entry.ReferenceId);
        command.Parameters.AddWithValue("$text",        entry.Text);
        command.Parameters.AddWithValue("$embedding",   blob);
        command.Parameters.AddWithValue("$embeddedAt",  entry.EmbeddedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[]           query,
        int               topK   = 5,
        string?           domain = null,
        CancellationToken ct     = default)
    {
        var entries = await LoadEntriesAsync(domain, ct);

        return entries
               .Select(entry => new VectorSearchResult(entry, CosineSimilarity(query, entry.Embedding)))
               .OrderByDescending(result => result.Score)
               .Take(topK)
               .ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM vectors WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByReferenceAsync(string domain, string referenceId, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM vectors WHERE domain = $domain AND referenceId = $referenceId";
        command.Parameters.AddWithValue("$domain",      domain);
        command.Parameters.AddWithValue("$referenceId", referenceId);

        await command.ExecuteNonQueryAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Schema bootstrap
    // -----------------------------------------------------------------------

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS vectors (
                id          TEXT PRIMARY KEY,
                domain      TEXT NOT NULL,
                referenceId TEXT NOT NULL,
                text        TEXT NOT NULL,
                embedding   BLOB NOT NULL,
                embeddedAt  TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Vectors_Domain     ON vectors(domain);
            CREATE INDEX IF NOT EXISTS IX_Vectors_Reference  ON vectors(domain, referenceId);
            """;
        command.ExecuteNonQuery();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<List<VectorEntry>> LoadEntriesAsync(string? domain, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();

        if (domain is null)
        {
            command.CommandText =
                "SELECT id, domain, referenceId, text, embedding, embeddedAt FROM vectors";
        }
        else
        {
            command.CommandText =
                "SELECT id, domain, referenceId, text, embedding, embeddedAt FROM vectors WHERE domain = $domain";
            command.Parameters.AddWithValue("$domain", domain);
        }

        var entries = new List<VectorEntry>();

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var blob      = (byte[])reader["embedding"];
            var embedding = MemoryMarshal.Cast<byte, float>(new Span<byte>(blob)).ToArray();

            entries.Add(new VectorEntry
                        {
                            Id          = (string)reader["id"]
                          , Domain      = (string)reader["domain"]
                          , ReferenceId = (string)reader["referenceId"]
                          , Text        = (string)reader["text"]
                          , Embedding   = embedding
                          , EmbeddedAt  = DateTimeOffset.Parse((string)reader["embeddedAt"])
                        });
        }

        return entries;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot  = 0f
            , magA = 0f
            , magB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        return magA == 0f || magB == 0f
                   ? 0f
                   : dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
    }
}
