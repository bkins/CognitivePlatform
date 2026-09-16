using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed partial class TtrImportPlanner
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredSchema =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Entry"]       = ["Id", "Title", "Text", "CreateDateTime", "JournalId", "MoodId", "Image", "ImageFileName", "Video", "VideoFileName", "OriginalJournalId"]
          , ["Journal"]     = ["Id", "Title", "JournalTypeId"]
          , ["JournalType"] = ["Id", "Title"]
          , ["Mood"]        = ["Id", "Title", "Emoji"]
          , ["Media"]       = ["Id", "MediaBytes", "MediaFileName", "Type", "EntryId"]
          , ["Notification"] = ["Id"]
        };

    private readonly TtrContentNormalizer _normalizer;

    public TtrImportPlanner(TtrContentNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public async Task<TtrImportPlan> PlanAsync(TtrImportPlanRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var sourceHash = await ComputeFileSha256Async(request.DatabasePath, cancellationToken);
        if (!sourceHash.Equals(request.ExpectedDatabaseSha256, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException($"Source database SHA-256 mismatch. Expected {request.ExpectedDatabaseSha256}, found {sourceHash}.");

        var timeZone = ResolveTimeZone(request.SourceTimeZoneId);
        var connectionString = new SqliteConnectionStringBuilder
                               {
                                   DataSource = request.DatabasePath
                                 , Mode       = SqliteOpenMode.ReadOnly
                                 , Pooling    = false
                               }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var schemaFingerprint = await ValidateAndFingerprintSchemaAsync(connection, cancellationToken);
        var journals          = await ReadJournalsAsync(connection, cancellationToken);
        var journalTypes      = await ReadStringLookupAsync(connection, "JournalType", "Title", cancellationToken);
        var moods             = await ReadMoodsAsync(connection, cancellationToken);
        var mediaByEntry      = await ReadMediaAsync(connection, cancellationToken);
        var findings          = new List<TtrImportFinding>();
        var plannedMedia      = new List<TtrPlannedMedia>();
        var resolvedFiles     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries           = new List<TtrPlannedEntry>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Text, CreateDateTime, JournalId, MoodId, OriginalJournalId, Image, ImageFileName, Video, VideoFileName FROM Entry ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceEntryId    = reader.GetInt64(0);
            var title            = reader.IsDBNull(1) ? null : reader.GetString(1);
            var body             = reader.IsDBNull(2) ? null : reader.GetString(2);
            var ticks            = reader.GetInt64(3);
            var journalId        = reader.GetInt64(4);
            var moodId           = reader.GetInt64(5);
            var originalJournalId = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
            var entryFindings    = new List<TtrImportFinding>();
            if (HasLegacyMediaValue(reader, 7) || HasLegacyMediaValue(reader, 8) || HasLegacyMediaValue(reader, 9) || HasLegacyMediaValue(reader, 10))
                entryFindings.Add(Blocking("legacy-entry-media-unsupported", "Legacy Entry image/video columns contain data; execution requires an explicit mapping.", sourceEntryId));

            var createdUtc = ConvertTicks(ticks, timeZone, sourceEntryId, entryFindings);
            journals.TryGetValue(journalId, out var journal);
            if (journal is null)
                entryFindings.Add(Blocking("journal-missing", $"Journal {journalId} does not exist.", sourceEntryId));

            string? journalTypeTitle = null;
            if (journal is not null && !journalTypes.TryGetValue(journal.JournalTypeId, out journalTypeTitle))
                entryFindings.Add(Blocking("journal-type-missing", $"Journal type {journal.JournalTypeId} does not exist.", sourceEntryId));

            moods.TryGetValue(moodId, out var mood);
            if (mood is null)
                entryFindings.Add(Blocking("mood-missing", $"Mood {moodId} does not exist.", sourceEntryId));

            var normalized = _normalizer.Normalize(title, body);
            foreach (var warning in normalized.Warnings)
                entryFindings.Add(new TtrImportFinding { Code = warning, Message = $"Content normalization reported {warning}.", SourceEntryId = sourceEntryId });
            if (normalized.Markdown.Length == 0)
                entryFindings.Add(Blocking("content-empty", "Both the source title and body are empty after normalization.", sourceEntryId));

            var tags = new List<string> { "source:ttr" };
            if (!string.IsNullOrWhiteSpace(journal?.Title)) tags.Add($"journal:{NormalizeTagValue(journal.Title)}");
            if (!string.IsNullOrWhiteSpace(journalTypeTitle)) tags.Add($"journal-type:{NormalizeTagValue(journalTypeTitle)}");

            if (mediaByEntry.TryGetValue(sourceEntryId, out var sourceMedia))
            {
                foreach (var media in sourceMedia.OrderBy(media => media.Id))
                {
                    var planned = await PlanMediaAsync(media, request, entryFindings, resolvedFiles, cancellationToken);
                    plannedMedia.Add(planned);
                }
            }

            for (var inlineIndex = 0; inlineIndex < normalized.InlineMedia.Count; inlineIndex++)
            {
                var inline = normalized.InlineMedia[inlineIndex];
                plannedMedia.Add(new TtrPlannedMedia
                                 {
                                     SourceEntryId    = sourceEntryId
                                   , InlineMediaIndex = inlineIndex
                                   , AttachmentId     = TtrDeterministicIdentity.CreateAttachmentId(request.LogicalSourceInstance, sourceEntryId, $"inline-{inlineIndex}").ToString()
                                   , LogicalFileName  = $"entry-{sourceEntryId}-inline-{inlineIndex}{ExtensionFor(inline.ContentType)}"
                                   , ContentType      = inline.ContentType
                                   , SizeBytes        = inline.Bytes.LongLength
                                   , ContentSha256    = ComputeSha256(inline.Bytes)
                                 });
            }

            findings.AddRange(entryFindings);
            var sourceContent = $"{title ?? string.Empty}\n{body ?? string.Empty}";
            entries.Add(new TtrPlannedEntry
                        {
                            SourceEntryId           = sourceEntryId
                          , OriginalJournalId       = originalJournalId
                          , EntryId                 = TtrDeterministicIdentity.CreateEntryId(request.LogicalSourceInstance, sourceEntryId).ToString("N")
                          , RevisionId              = TtrDeterministicIdentity.CreateInitialRevisionId(request.LogicalSourceInstance, sourceEntryId).ToString("N")
                          , CreatedUtc              = createdUtc
                          , NormalizedText          = normalized.Markdown
                          , SourceContentSha256     = ComputeSha256(Encoding.UTF8.GetBytes(sourceContent))
                          , NormalizedContentSha256 = ComputeSha256(Encoding.UTF8.GetBytes(normalized.Markdown))
                          , Tags                    = tags
                          , Mood                    = CombineMood(mood)
                          , MoodScore               = null
                          , MoodLevel               = null
                          , SourceJournalTitle      = journal?.Title
                          , SourceJournalTypeTitle  = journalTypeTitle
                          , Disposition             = entryFindings.Any(finding => finding.IsBlocking) ? "Blocked" : "Ready"
                        });
        }

        var ignoredFiles = Directory.EnumerateFiles(request.RecoveryBundlePath, "*.mp4", SearchOption.AllDirectories)
                                    .Where(path => !resolvedFiles.Contains(Path.GetFullPath(path)))
                                    .Select(path => Path.GetRelativePath(request.RecoveryBundlePath, path))
                                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                                    .ToArray();
        foreach (var ignoredFile in ignoredFiles)
            findings.Add(new TtrImportFinding { Code = "orphan-media-ignored", Message = $"Unreferenced recovery file is ignored: {ignoredFile}." });

        var plan = new TtrImportPlan
                   {
                       SourceSha256          = sourceHash
                     , SchemaFingerprint     = schemaFingerprint
                     , LogicalSourceInstance = request.LogicalSourceInstance
                     , SourceTimeZoneId      = request.SourceTimeZoneId
                     , TargetPartition       = request.TargetPartition
                     , Entries               = entries
                     , Media                 = plannedMedia.OrderBy(media => media.SourceEntryId).ThenBy(media => media.SourceMediaId).ThenBy(media => media.InlineMediaIndex).ToArray()
                     , Findings              = findings.OrderBy(finding => finding.SourceEntryId).ThenBy(finding => finding.SourceMediaId).ThenBy(finding => finding.Code).ToArray()
                     , IgnoredFiles          = ignoredFiles
                   };
        plan.PlanSha256 = ComputePlanHash(plan);
        plan.HumanReadableReport = BuildHumanReadableReport(plan);
        return plan;
    }

    private static async Task<TtrPlannedMedia> PlanMediaAsync(TtrSourceMedia media, TtrImportPlanRequest request, List<TtrImportFinding> findings, HashSet<string> resolvedFiles, CancellationToken cancellationToken)
    {
        byte[] bytes;
        string? resolvedPath = null;
        if (media.Type == 0 && media.Bytes is not null)
        {
            bytes = media.Bytes;
        }
        else
        {
            resolvedPath = ResolveMediaPath(request.RecoveryBundlePath, media.FileName);
            if (resolvedPath is null)
            {
                findings.Add(new TtrImportFinding
                             {
                                 Code          = "media-file-missing"
                               , Message       = $"Referenced media file was not recovered: {media.FileName}."
                               , SourceEntryId = media.EntryId
                               , SourceMediaId = media.Id
                               , IsBlocking    = true
                             });
                return new TtrPlannedMedia
                       {
                           SourceEntryId  = media.EntryId
                         , SourceMediaId  = media.Id
                         , AttachmentId   = TtrDeterministicIdentity.CreateAttachmentId(request.LogicalSourceInstance, media.EntryId, media.Id.ToString()).ToString()
                         , LogicalFileName = Path.GetFileName(media.FileName)
                         , Disposition     = "Blocked"
                       };
            }
            resolvedPath = Path.GetFullPath(resolvedPath);
            resolvedFiles.Add(resolvedPath);
            bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
        }

        var contentType = DetectContentType(bytes);
        if (contentType == "application/octet-stream")
            findings.Add(new TtrImportFinding
                         {
                             Code          = "media-type-unknown"
                           , Message       = $"Media signature is not recognized: {media.FileName}."
                           , SourceEntryId = media.EntryId
                           , SourceMediaId = media.Id
                           , IsBlocking    = true
                         });

        return new TtrPlannedMedia
               {
                   SourceEntryId   = media.EntryId
                 , SourceMediaId   = media.Id
                 , AttachmentId    = TtrDeterministicIdentity.CreateAttachmentId(request.LogicalSourceInstance, media.EntryId, media.Id.ToString()).ToString()
                 , LogicalFileName = Path.GetFileName(media.FileName)
                 , ContentType     = contentType
                 , SizeBytes       = bytes.LongLength
                 , ContentSha256   = ComputeSha256(bytes)
                 , ResolvedPath    = resolvedPath is null ? null : Path.GetRelativePath(request.RecoveryBundlePath, resolvedPath)
                 , Disposition     = contentType == "application/octet-stream" ? "Blocked" : "Ready"
               };
    }

    private static string? ResolveMediaPath(string bundlePath, string sourceFileName)
    {
        var sourceBaseName = Path.GetFileName(sourceFileName);
        var files = Directory.EnumerateFiles(bundlePath, "*", SearchOption.AllDirectories).ToArray();
        var exact = files.FirstOrDefault(path => Path.GetFileName(path).Equals(sourceBaseName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var safeName = CollapseUnderscoresRegex().Replace(InvalidFileNameRegex().Replace(sourceBaseName, "_"), "_");
        var matches  = files.Where(path => CollapseUnderscoresRegex().Replace(InvalidFileNameRegex().Replace(Path.GetFileName(path), "_"), "_")
                                                  .Equals(safeName, StringComparison.OrdinalIgnoreCase))
                            .Take(2)
                            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static DateTimeOffset ConvertTicks(long ticks, TimeZoneInfo timeZone, long sourceEntryId, List<TtrImportFinding> findings)
    {
        try
        {
            var local = new DateTime(ticks, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(local))
                throw new TtrImportValidationException($"Entry {sourceEntryId} has an invalid local timestamp.");
            if (timeZone.IsAmbiguousTime(local))
                throw new TtrImportValidationException($"Entry {sourceEntryId} has an ambiguous local timestamp.");
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or TtrImportValidationException)
        {
            findings.Add(Blocking("timestamp-invalid", exception.Message, sourceEntryId));
            return DateTimeOffset.MinValue;
        }
    }

    private static async Task<string> ValidateAndFingerprintSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var canonical = new StringBuilder();
        foreach (var requiredTable in RequiredSchema.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{requiredTable.Key}\");";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var columns = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));
            if (columns.Count == 0) throw new TtrImportValidationException($"Required source table is missing: {requiredTable.Key}.");
            var missing = requiredTable.Value.Where(column => !columns.Contains(column, StringComparer.Ordinal)).ToArray();
            if (missing.Length > 0) throw new TtrImportValidationException($"Source table {requiredTable.Key} is missing required columns: {string.Join(", ", missing)}.");
            canonical.Append(requiredTable.Key).Append(':').AppendJoin(',', columns).Append(';');
        }
        return ComputeSha256(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static async Task<Dictionary<long, TtrJournalLookup>> ReadJournalsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, TtrJournalLookup>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, JournalTypeId FROM Journal;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetInt64(0)] = new TtrJournalLookup(reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2));
        return result;
    }

    private static async Task<Dictionary<long, string?>> ReadStringLookupAsync(SqliteConnection connection, string table, string valueColumn, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, string?>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, \"{valueColumn}\" FROM \"{table}\";";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetInt64(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return result;
    }

    private static async Task<Dictionary<long, TtrMoodLookup>> ReadMoodsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, TtrMoodLookup>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Emoji FROM Mood;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetInt64(0)] = new TtrMoodLookup(reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        return result;
    }

    private static async Task<Dictionary<long, List<TtrSourceMedia>>> ReadMediaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, List<TtrSourceMedia>>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, MediaBytes, MediaFileName, Type, EntryId FROM Media;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new TtrSourceMedia(reader.GetInt64(0), reader.IsDBNull(1) ? null : (byte[])reader[1], reader.IsDBNull(2) ? string.Empty : reader.GetString(2), reader.GetInt32(3), reader.GetInt64(4));
            if (!result.TryGetValue(item.EntryId, out var list)) result[item.EntryId] = list = [];
            list.Add(item);
        }
        return result;
    }

    public static string ComputePlanHash(TtrImportPlan plan)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            plan.SourceSha256, plan.SchemaFingerprint, plan.LogicalSourceInstance, plan.SourceTimeZoneId, plan.TargetPartition,
            plan.Entries, plan.Media, plan.Findings, plan.IgnoredFiles
        });
        return ComputeSha256(Encoding.UTF8.GetBytes(canonical));
    }

    private static string BuildHumanReadableReport(TtrImportPlan plan)
    {
        var warningCount = plan.Findings.Count(finding => !finding.IsBlocking);
        return $"TtR import plan {plan.PlanSha256}\n"
             + $"Source: {plan.SourceSha256}\n"
             + $"Target partition: {plan.TargetPartition}\n"
             + $"Entries: {plan.Entries.Count} total, {plan.ReadyEntryCount} ready, {plan.BlockedEntryCount} blocked\n"
             + $"Attachments: {plan.Media.Count}\n"
             + $"Findings: {warningCount} warnings, {plan.Findings.Count(finding => finding.IsBlocking)} blocking\n"
             + $"Ignored files: {plan.IgnoredFiles.Count}";
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string DetectContentType(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp") return "video/mp4";
        if (bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8)) return "image/gif";
        return "application/octet-stream";
    }

    private static string NormalizeTagValue(string value)
    {
        return TagSeparatorRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
    }

    private static string? CombineMood(TtrMoodLookup? mood)
    {
        if (mood is null) return null;
        return string.Join(' ', new[] { mood.Title, mood.Emoji }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool HasLegacyMediaValue(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return false;
        var value = reader.GetValue(ordinal);
        return value switch
               {
                   byte[] bytes => bytes.Length > 0
                 , string text  => !string.IsNullOrWhiteSpace(text)
                 , _            => true
               };
    }

    private static string ExtensionFor(string contentType)
    {
        return contentType switch { "image/png" => ".png", "image/jpeg" or "image/jpg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp", _ => ".bin" };
    }

    private static TtrImportFinding Blocking(string code, string message, long sourceEntryId)
    {
        return new TtrImportFinding { Code = code, Message = message, SourceEntryId = sourceEntryId, IsBlocking = true };
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException exception) { throw new TtrImportValidationException($"Source timezone was not found: {id}. {exception.Message}"); }
    }

    private static void ValidateRequest(TtrImportPlanRequest request)
    {
        if (!File.Exists(request.DatabasePath)) throw new TtrImportValidationException($"Source database does not exist: {request.DatabasePath}.");
        if (!Directory.Exists(request.RecoveryBundlePath)) throw new TtrImportValidationException($"Recovery bundle does not exist: {request.RecoveryBundlePath}.");
        if (request.ExpectedDatabaseSha256.Length != 64) throw new TtrImportValidationException("Expected source SHA-256 must contain 64 hexadecimal characters.");
        if (string.IsNullOrWhiteSpace(request.LogicalSourceInstance)) throw new TtrImportValidationException("Logical source instance is required.");
        if (string.IsNullOrWhiteSpace(request.SourceTimeZoneId)) throw new TtrImportValidationException("Source timezone is required.");
        if (string.IsNullOrWhiteSpace(request.TargetPartition)) throw new TtrImportValidationException("Target partition is required.");
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex TagSeparatorRegex();

    [GeneratedRegex("[<>:\"/\\\\|?*]")]
    private static partial Regex InvalidFileNameRegex();

    [GeneratedRegex("_+")]
    private static partial Regex CollapseUnderscoresRegex();
}
