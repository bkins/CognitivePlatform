using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Companion;

/// <summary>Optional explicitly configured Axiom metadata context. Never creates, migrates or writes state.</summary>
public sealed class CompanionMetadataReader
{
    public async Task<CompanionMetadata> ReadAsync(CompanionWorkspace workspace, string relativePath, Guid? documentId, CancellationToken cancellationToken)
    {
        CompanionMetadata Unavailable(string status) => new(false, status, null, [], [], null, DateTimeOffset.UtcNow, []);
        if (documentId is null) return Unavailable("Ordinary Markdown has no enrolled identity.");
        if (string.IsNullOrWhiteSpace(workspace.MetadataDatabasePath)) return Unavailable("Metadata context is not published for this workspace.");
        try
        {
            if (!Path.IsPathFullyQualified(workspace.MetadataDatabasePath) || !File.Exists(workspace.MetadataDatabasePath))
                return Unavailable("Published metadata is unavailable.");
            for (var ancestor = workspace.MetadataDatabasePath; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
                if (File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint)) return Unavailable("Linked metadata paths are not published.");
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = workspace.MetadataDatabasePath
              , Mode = SqliteOpenMode.ReadOnly
              , Pooling = false
              , DefaultTimeout = 2
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: true);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT Version FROM SchemaInfo LIMIT 1;";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) is not (8 or 9)) return Unavailable("Metadata schema is unsupported.");
            command.Parameters.AddWithValue("$id", documentId.Value.ToString("D"));
            command.CommandText = "SELECT FilePath FROM DocumentMetadata WHERE DocumentId=$id LIMIT 2;";
            var observedPaths = new List<string>();
            await using (var identities = await command.ExecuteReaderAsync(cancellationToken))
                while (await identities.ReadAsync(cancellationToken)) observedPaths.Add(identities.GetString(0));
            var sourcePath = CompanionWorkspaceReader.Resolve(workspace, relativePath);
            if (observedPaths.Count != 1 || !string.Equals(Path.GetFullPath(observedPaths[0]), sourcePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return Unavailable("Identity is unobserved, ambiguous or differs from the saved metadata path.");
            command.CommandText = "SELECT TagsJson,Type,AliasesJson,Revision FROM DocumentClassification WHERE DocumentId=$id;";
            string[] tags = [], aliases = [];
            string? type = null;
            long? revision = null;
            await using (var row = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await row.ReadAsync(cancellationToken))
                {
                    tags = Values(row.GetString(0));
                    type = row.IsDBNull(1) ? null : row.GetString(1);
                    aliases = Values(row.GetString(2));
                    revision = row.GetInt64(3);
                    if (type?.Length > 1024) return Unavailable("Metadata bounds exceeded.");
                }
            }
            command.CommandText = "SELECT SourceId,TargetId,Kind,Revision FROM DocumentRelationships WHERE SourceId=$id OR TargetId=$id LIMIT 65;";
            var relationships = new List<CompanionRelationship>();
            await using (var rows = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await rows.ReadAsync(cancellationToken))
                {
                    var source = Guid.Parse(rows.GetString(0));
                    var target = Guid.Parse(rows.GetString(1));
                    var kind = rows.GetString(2);
                    if (kind.Length > 1024 || relationships.Count >= 64) return Unavailable("Metadata bounds exceeded.");
                    relationships.Add(new(source == documentId ? target : source, source == documentId ? "outgoing" : "incoming", kind, rows.GetInt64(3)));
                }
            }
            return new(true, "Saved metadata snapshot; related IDs do not grant access to unpublished documents.", type, tags, aliases, revision, DateTimeOffset.UtcNow, relationships.ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException or ArgumentException)
        { return Unavailable("Published metadata could not be read safely; Markdown remains available."); }
    }

    private static string[] Values(string json)
    {
        if (json.Length > 32_768) throw new InvalidOperationException();
        var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
        if (values.Length > 128 || values.Any(value => value is null || value.Length > 1024)) throw new InvalidOperationException();
        return values;
    }
}
