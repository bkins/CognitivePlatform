using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrMediaContentReader : ITtrMediaContentReader
{
    private readonly TtrContentNormalizer _normalizer;

    public TtrMediaContentReader(TtrContentNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public async Task<byte[]> ReadAsync(TtrImportPlanRequest source, TtrPlannedMedia media, CancellationToken cancellationToken = default)
    {
        if (media.ResolvedPath is not null)
        {
            var bundleRoot = Path.GetFullPath(source.RecoveryBundlePath);
            var path       = Path.GetFullPath(Path.Combine(bundleRoot, media.ResolvedPath));
            if (!path.StartsWith(bundleRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new TtrImportValidationException($"Resolved media path escapes the recovery bundle: {media.ResolvedPath}.");
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }

        var connectionString = new SqliteConnectionStringBuilder
                               {
                                   DataSource = source.DatabasePath
                                 , Mode       = SqliteOpenMode.ReadOnly
                                 , Pooling    = false
                               }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (media.SourceMediaId is not null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MediaBytes FROM Media WHERE Id = $id AND EntryId = $entryId;";
            command.Parameters.AddWithValue("$id", media.SourceMediaId.Value);
            command.Parameters.AddWithValue("$entryId", media.SourceEntryId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is byte[] bytes) return bytes;
            throw new TtrImportValidationException($"Embedded media {media.SourceMediaId} for entry {media.SourceEntryId} is missing.");
        }

        if (media.InlineMediaIndex is not null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Title, Text FROM Entry WHERE Id = $entryId;";
            command.Parameters.AddWithValue("$entryId", media.SourceEntryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new TtrImportValidationException($"Entry {media.SourceEntryId} is missing while reading inline media.");
            var normalized = _normalizer.Normalize(reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            if (media.InlineMediaIndex.Value >= normalized.InlineMedia.Count)
                throw new TtrImportValidationException($"Inline media {media.InlineMediaIndex} for entry {media.SourceEntryId} is missing.");
            return normalized.InlineMedia[media.InlineMediaIndex.Value].Bytes;
        }

        throw new TtrImportValidationException($"Planned media for entry {media.SourceEntryId} has no source locator.");
    }
}
