using System.Text;
using System.Text.Json;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Domains.Backlog;

public sealed class SqliteBacklogBoardService : IBacklogBoardService
{
    private const string WorkspaceKey = "cp-universe";
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private bool _isInitialized;

    public SqliteBacklogBoardService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<BacklogBoardDto> GetBoardAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var stories = new List<BacklogStoryDto>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, DisplayId, Title, Description, ProjectKey, AreaKey, StreamKey, ItemTypeKey, ColumnKey, Rank, Priority, Revision, PropertiesJson
                FROM BacklogStories WHERE IsArchived = 0 ORDER BY ColumnKey, Rank, DisplayId;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) stories.Add(ReadStory(reader));
        }

        return new BacklogBoardDto(
            stories,
            await ReadReferencesAsync(connection, "project", cancellationToken),
            await ReadReferencesAsync(connection, "area", cancellationToken),
            await ReadReferencesAsync(connection, "stream", cancellationToken),
            await ReadReferencesAsync(connection, "column", cancellationToken),
            await ReadReferencesAsync(connection, "type", cancellationToken),
            await ReadReferencesAsync(connection, "property", cancellationToken));
    }

    public async Task<BacklogStoryDto> CreateStoryAsync(CreateBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        ValidateStory(request);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ValidateReferencesAsync(connection, request.ProjectKey, request.AreaKey, request.StreamKey, request.ItemTypeKey, request.ColumnKey, cancellationToken);
        var nextNumber = await GetNextDisplayNumberAsync(connection, cancellationToken);
        var displayId = string.IsNullOrWhiteSpace(request.DisplayId) ? $"STORY-{nextNumber}" : request.DisplayId.Trim();
        var story = new BacklogStoryDto(Guid.NewGuid(), displayId, request.Title.Trim(), request.Description.Trim(), request.ProjectKey.Trim(), request.AreaKey.Trim(), request.StreamKey?.Trim(), request.ItemTypeKey.Trim(), request.ColumnKey.Trim(), await GetAppendRankAsync(connection, request.ColumnKey, cancellationToken), request.Priority, 1, NormalizeProperties(request.Properties));
        await InsertStoryAsync(connection, story, cancellationToken);
        await WriteAuditAsync(connection, "story", story.Id.ToString(), "created", request.Actor, null, story, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return story;
    }

    public async Task<BacklogStoryDto> UpdateStoryAsync(Guid storyId, UpdateBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var createShape = new CreateBacklogStoryRequest(request.ProjectKey, request.AreaKey, request.ItemTypeKey, "backlog", request.Title, request.Description, request.Priority, request.StreamKey, request.Properties, request.Actor);
        ValidateStory(createShape);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await GetStoryAsync(connection, storyId, cancellationToken) ?? throw new BacklogValidationException("The requested story does not exist.");
        EnsureRevision(existing, request.ExpectedRevision);
        await ValidateReferencesAsync(connection, request.ProjectKey, request.AreaKey, request.StreamKey, request.ItemTypeKey, existing.ColumnKey, cancellationToken);
        var updated = existing with
        {
            Title = request.Title.Trim(), Description = request.Description.Trim(), ProjectKey = request.ProjectKey.Trim(), AreaKey = request.AreaKey.Trim(), StreamKey = request.StreamKey?.Trim(), ItemTypeKey = request.ItemTypeKey.Trim(), Priority = request.Priority, Revision = existing.Revision + 1, Properties = NormalizeProperties(request.Properties)
        };
        await UpdateStoryAsync(connection, updated, cancellationToken);
        await WriteAuditAsync(connection, "story", storyId.ToString(), "updated", request.Actor, existing, updated, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<BacklogStoryDto> MoveStoryAsync(Guid storyId, MoveBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await GetStoryAsync(connection, storyId, cancellationToken) ?? throw new BacklogValidationException("The requested story does not exist.");
        EnsureRevision(existing, request.ExpectedRevision);
        await ValidateReferenceAsync(connection, "column", request.ColumnKey, cancellationToken);
        var rank = await GetMovedRankAsync(connection, storyId, request.ColumnKey, request.BeforeStoryId, request.AfterStoryId, cancellationToken);
        var updated = existing with { ColumnKey = request.ColumnKey.Trim(), Rank = rank, Revision = existing.Revision + 1 };
        await UpdateStoryAsync(connection, updated, cancellationToken);
        await WriteAuditAsync(connection, "story", storyId.ToString(), "moved", request.Actor, existing, updated, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<BacklogStoryDto> ArchiveStoryAsync(Guid storyId, ArchiveBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await GetStoryAsync(connection, storyId, includeArchived: false, cancellationToken)
                       ?? throw new BacklogValidationException("The requested active story does not exist.");
        EnsureRevision(existing, request.ExpectedRevision);
        if (!string.Equals(existing.ColumnKey, "done", StringComparison.OrdinalIgnoreCase))
            throw new BacklogValidationException("Only Done stories can be archived.");
        await SetStoryArchivedAsync(connection, storyId, true, cancellationToken);
        await WriteAuditAsync(connection, "story", storyId.ToString(), "archived", request.Actor, existing, existing, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return existing;
    }

    public async Task<BacklogStoryDto> UnarchiveStoryAsync(Guid storyId, string? actor = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var archived = await GetStoryAsync(connection, storyId, includeArchived: true, cancellationToken)
                       ?? throw new BacklogValidationException("The requested archived story does not exist.");
        if (!await IsStoryArchivedAsync(connection, storyId, cancellationToken))
            throw new BacklogValidationException("The requested story is already active.");
        await SetStoryArchivedAsync(connection, storyId, false, cancellationToken);
        await WriteAuditAsync(connection, "story", storyId.ToString(), "unarchived", actor, archived, archived, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return archived;
    }

    public async Task<IReadOnlyList<BacklogStoryDto>> GetArchivedStoriesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, DisplayId, Title, Description, ProjectKey, AreaKey, StreamKey, ItemTypeKey, ColumnKey, Rank, Priority, Revision, PropertiesJson FROM BacklogStories WHERE IsArchived = 1 ORDER BY ColumnKey, Rank, DisplayId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var stories = new List<BacklogStoryDto>();
        while (await reader.ReadAsync(cancellationToken)) stories.Add(ReadStory(reader));
        return stories;
    }

    public async Task<BacklogReferenceDto> CreateReferenceAsync(string referenceType, CreateBacklogReferenceRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var normalizedType = NormalizeReferenceType(referenceType);
        if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Name)) throw new BacklogValidationException("A key and name are required.");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO BacklogReferences (ReferenceType, Key, Name, SortOrder, IsArchived) VALUES ($type, $key, $name, $order, 0);";
        command.Parameters.AddWithValue("$type", normalizedType); command.Parameters.AddWithValue("$key", request.Key.Trim()); command.Parameters.AddWithValue("$name", request.Name.Trim()); command.Parameters.AddWithValue("$order", request.SortOrder);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19) { throw new BacklogValidationException($"{normalizedType} key '{request.Key}' already exists."); }
        var result = new BacklogReferenceDto(request.Key.Trim(), request.Name.Trim(), request.SortOrder);
        await WriteAuditAsync(connection, normalizedType, result.Key, "created", request.Actor, null, result, cancellationToken);
        return result;
    }

    public async Task<BacklogReferenceDto> UpdateReferenceAsync(string referenceType, string key, UpdateBacklogReferenceRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var normalizedType = NormalizeReferenceType(referenceType);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(request.Name)) throw new BacklogValidationException("A key and name are required.");
        await using var connection = await OpenAsync(cancellationToken);
        var previous = await GetReferenceAsync(connection, normalizedType, key, cancellationToken)
                       ?? throw new BacklogValidationException($"The {normalizedType} '{key}' does not exist.");
        var result = new BacklogReferenceDto(previous.Key, request.Name.Trim(), request.SortOrder, request.IsArchived);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE BacklogReferences SET Name = $name, SortOrder = $sortOrder, IsArchived = $isArchived WHERE ReferenceType = $type AND Key = $key;";
        command.Parameters.AddWithValue("$name", result.Name); command.Parameters.AddWithValue("$sortOrder", result.SortOrder); command.Parameters.AddWithValue("$isArchived", result.IsArchived ? 1 : 0); command.Parameters.AddWithValue("$type", normalizedType); command.Parameters.AddWithValue("$key", result.Key);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteAuditAsync(connection, normalizedType, result.Key, result.IsArchived ? "archived" : "updated", request.Actor, previous, result, cancellationToken);
        return result;
    }

    public async Task<BacklogIntakeDto> SubmitIntakeAsync(SubmitBacklogIntakeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Description)) throw new BacklogValidationException("Intake description is required.");
        var result = new BacklogIntakeDto(Guid.NewGuid(), request.Kind.Trim().ToLowerInvariant(), request.Description.Trim(), request.Source.Trim(), DateTimeOffset.UtcNow, "submitted", null);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO BacklogIntake (Id, Kind, Description, Source, SubmittedAtUtc, State, PromotedStoryId) VALUES ($id, $kind, $description, $source, $submittedAtUtc, $state, NULL);";
        command.Parameters.AddWithValue("$id", result.Id.ToString()); command.Parameters.AddWithValue("$kind", result.Kind); command.Parameters.AddWithValue("$description", result.Description); command.Parameters.AddWithValue("$source", result.Source); command.Parameters.AddWithValue("$submittedAtUtc", result.SubmittedAtUtc.ToString("O")); command.Parameters.AddWithValue("$state", result.State);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteAuditAsync(connection, "intake", result.Id.ToString(), "submitted", request.Actor, null, result, cancellationToken);
        return result;
    }

    public async Task<BacklogStoryDto> PromoteIntakeAsync(Guid intakeId, PromoteBacklogIntakeRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Id, Kind, Description, Source, SubmittedAtUtc, State, PromotedStoryId FROM BacklogIntake WHERE Id = $id;"; command.Parameters.AddWithValue("$id", intakeId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new BacklogValidationException("The requested intake record does not exist.");
        var intake = ReadIntake(reader);
        if (intake.PromotedStoryId is not null) throw new BacklogConflictException("The intake record has already been promoted.");
        var story = await CreateStoryAsync(request.Story with { Actor = request.Actor ?? request.Story.Actor }, cancellationToken);
        await using var update = connection.CreateCommand(); update.CommandText = "UPDATE BacklogIntake SET State = 'promoted', PromotedStoryId = $storyId WHERE Id = $id;"; update.Parameters.AddWithValue("$storyId", story.Id.ToString()); update.Parameters.AddWithValue("$id", intakeId.ToString()); await update.ExecuteNonQueryAsync(cancellationToken);
        await WriteAuditAsync(connection, "intake", intakeId.ToString(), "promoted", request.Actor, intake, story, cancellationToken);
        return story;
    }

    public async Task<IReadOnlyList<BacklogIntakeDto>> GetIntakeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken); await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT Id, Kind, Description, Source, SubmittedAtUtc, State, PromotedStoryId FROM BacklogIntake ORDER BY SubmittedAtUtc DESC;"; await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<BacklogIntakeDto>(); while (await reader.ReadAsync(cancellationToken)) results.Add(ReadIntake(reader)); return results;
    }

    public async Task<IReadOnlyList<BacklogAuditEventDto>> GetHistoryAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken); await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT Id, EntityType, EntityId, Action, Actor, CorrelationId, OccurredAtUtc, BeforeJson, AfterJson FROM BacklogAuditEvents WHERE EntityType = 'story' AND EntityId = $id ORDER BY OccurredAtUtc DESC;"; command.Parameters.AddWithValue("$id", storyId.ToString()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<BacklogAuditEventDto>(); while (await reader.ReadAsync(cancellationToken)) results.Add(new BacklogAuditEventDto(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6)), reader.GetString(7), reader.GetString(8))); return results;
    }

    public async Task<string> ExportMarkdownAsync(CancellationToken cancellationToken = default)
    {
        var board = await GetBoardAsync(cancellationToken);
        var output = new StringBuilder("# Backlog Export\n\n> Generated on demand from the Backlog Board database. Do not edit as a source of truth.\n\n");
        foreach (var group in board.Stories.GroupBy(story => story.ColumnKey).OrderBy(group => group.Key))
        {
            output.AppendLine($"## {group.Key}");
            output.AppendLine();
            foreach (var story in group.OrderBy(story => story.Rank).ThenBy(story => story.DisplayId))
            {
                output.AppendLine($"### {story.DisplayId} — {story.Title}");
                output.AppendLine();
                output.AppendLine("| Field | Value |");
                output.AppendLine("| --- | --- |");
                output.AppendLine($"| Project | {Escape(story.ProjectKey)} |");
                output.AppendLine($"| Area | {Escape(story.AreaKey)} |");
                output.AppendLine($"| Stream | {Escape(story.StreamKey ?? string.Empty)} |");
                output.AppendLine($"| Type | {Escape(story.ItemTypeKey)} |");
                output.AppendLine($"| Column | {Escape(story.ColumnKey)} |");
                output.AppendLine($"| Priority | {story.Priority} |");
                output.AppendLine($"| Rank | {Escape(story.Rank)} |");
                output.AppendLine($"| Revision | {story.Revision} |");
                output.AppendLine();
                output.AppendLine("#### Description");
                output.AppendLine();
                output.AppendLine(story.Description.Trim());
                output.AppendLine();
                output.AppendLine("#### Properties");
                output.AppendLine();
                output.AppendLine("| Property | Value |");
                output.AppendLine("| --- | --- |");
                foreach (var property in story.Properties.OrderBy(property => property.Key, StringComparer.OrdinalIgnoreCase))
                    output.AppendLine($"| {Escape(property.Key)} | {Escape(property.Value)} |");
                output.AppendLine();
            }
        }
        return output.ToString();
    }

    public async Task<BacklogRecoveryBackup> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            var builder = new SqliteConnectionStringBuilder(_connectionString);
            var backupDirectory = Path.Combine(Path.GetDirectoryName(builder.DataSource)!, "backups");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, $"updated-backlog-before-restore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");
            await using var source = await OpenAsync(cancellationToken);
            await using var destination = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate;Pooling=False");
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
            var backup = new BacklogRecoveryBackup(backupPath, DateTimeOffset.UtcNow);
            await ValidateBackupAsync(backup, cancellationToken);
            return backup;
        }
        finally { _recoveryLock.Release(); }
    }

    public async Task ResetAsync(BacklogRecoveryBackup backup, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await ValidateBackupAsync(backup, cancellationToken);
        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM BacklogAuditEvents; DELETE FROM BacklogIntake; DELETE FROM BacklogStories; DELETE FROM BacklogReferences;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            await SeedDefaultsAsync(connection, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _recoveryLock.Release(); }
    }

    public async Task RestoreBackupAsync(BacklogRecoveryBackup backup, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var backupPath = await ValidateBackupAsync(backup, cancellationToken);
        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            await using var source = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
            await source.OpenAsync(cancellationToken);
            await using var destination = await OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }
        finally { _recoveryLock.Release(); }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return; await _initializationLock.WaitAsync(cancellationToken); try { if (_isInitialized) return; var builder = new SqliteConnectionStringBuilder(_connectionString); var directory = Path.GetDirectoryName(builder.DataSource); if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory!); await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = SchemaSql; await command.ExecuteNonQueryAsync(cancellationToken); await SeedDefaultsAsync(connection, cancellationToken); _isInitialized = true; } finally { _initializationLock.Release(); }
    }

    private Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString); return OpenConnectionAsync(connection, cancellationToken);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken) { await connection.OpenAsync(cancellationToken); return connection; }

    private async Task<string> ValidateBackupAsync(BacklogRecoveryBackup backup, CancellationToken cancellationToken)
    {
        var databasePath = Path.GetFullPath(new SqliteConnectionStringBuilder(_connectionString).DataSource);
        var backupDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(databasePath)!, "backups"));
        var backupPath = Path.GetFullPath(backup.Path);
        var expectedPrefix = backupDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!backupPath.StartsWithIgnoreCase(expectedPrefix)
            || backupPath.EqualsIgnoreCase(databasePath)
            || !File.Exists(backupPath))
            throw new BacklogValidationException("A verified backup in the updated-board backup directory is required for recovery.");

        await using var connection = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var integrity = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (!integrity.EqualsIgnoreCase("ok"))
            throw new BacklogValidationException("The recovery backup failed SQLite integrity validation.");

        return backupPath;
    }
    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static void EnsureRevision(BacklogStoryDto story, long revision) { if (story.Revision != revision) throw new BacklogConflictException("This story changed after you opened it. Reload the board and try again."); }
    private static IReadOnlyDictionary<string, string> NormalizeProperties(IReadOnlyDictionary<string, string>? properties) => properties?.Where(entry => !string.IsNullOrWhiteSpace(entry.Key)).ToDictionary(entry => entry.Key.Trim(), entry => entry.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static void ValidateStory(CreateBacklogStoryRequest request) { if (new[] { request.ProjectKey, request.AreaKey, request.ItemTypeKey, request.ColumnKey, request.Title, request.Description }.Any(string.IsNullOrWhiteSpace)) throw new BacklogValidationException("Project, area, type, column, title, and description are required."); if (request.Priority is < 1 or > 99) throw new BacklogValidationException("Priority must be between 1 and 99."); }
    private static string NormalizeReferenceType(string value) { var result = value.Trim().ToLowerInvariant(); return result is "project" or "area" or "stream" or "column" or "type" or "property" ? result : throw new BacklogValidationException("Unsupported reference type."); }

    private static BacklogStoryDto ReadStory(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetInt32(10), reader.GetInt64(11), JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(12)) ?? new Dictionary<string, string>());
    private static BacklogIntakeDto ReadIntake(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4)), reader.GetString(5), reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)));
    private static string ToJson(object? value) => value is null ? "null" : JsonSerializer.Serialize(value);

    private async Task<List<BacklogReferenceDto>> ReadReferencesAsync(SqliteConnection connection, string type, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT Key, Name, SortOrder, IsArchived FROM BacklogReferences WHERE ReferenceType = $type ORDER BY IsArchived, SortOrder, Name;"; command.Parameters.AddWithValue("$type", type); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<BacklogReferenceDto>(); while (await reader.ReadAsync(cancellationToken)) results.Add(new BacklogReferenceDto(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3))); return results; }
    private async Task<BacklogReferenceDto?> GetReferenceAsync(SqliteConnection connection, string type, string key, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT Key, Name, SortOrder, IsArchived FROM BacklogReferences WHERE ReferenceType = $type AND Key = $key;"; command.Parameters.AddWithValue("$type", type); command.Parameters.AddWithValue("$key", key.Trim()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? new BacklogReferenceDto(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3)) : null; }
    private async Task ValidateReferencesAsync(SqliteConnection connection, string project, string area, string? stream, string type, string column, CancellationToken cancellationToken) { await ValidateReferenceAsync(connection, "project", project, cancellationToken); await ValidateReferenceAsync(connection, "area", area, cancellationToken); if (!string.IsNullOrWhiteSpace(stream)) await ValidateReferenceAsync(connection, "stream", stream!, cancellationToken); await ValidateReferenceAsync(connection, "type", type, cancellationToken); await ValidateReferenceAsync(connection, "column", column, cancellationToken); }
    private async Task ValidateReferenceAsync(SqliteConnection connection, string type, string key, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(1) FROM BacklogReferences WHERE ReferenceType = $type AND Key = $key AND IsArchived = 0;"; command.Parameters.AddWithValue("$type", type); command.Parameters.AddWithValue("$key", key.Trim()); if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1) throw new BacklogValidationException($"Unknown active {type} '{key}'."); }
    private async Task<int> GetNextDisplayNumberAsync(SqliteConnection connection, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(1) + 1 FROM BacklogStories;"; return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)); }
    private async Task<string> GetAppendRankAsync(SqliteConnection connection, string column, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT Rank FROM BacklogStories WHERE ColumnKey = $column AND IsArchived = 0 ORDER BY Rank DESC LIMIT 1;"; command.Parameters.AddWithValue("$column", column.Trim()); var last = await command.ExecuteScalarAsync(cancellationToken) as string; return last is null ? "00000000000000000001" : (long.Parse(last) + 1).ToString("D20"); }
    private async Task<string> GetMovedRankAsync( SqliteConnection    connection
                                                 , Guid                movingStoryId
                                                 , string              column
                                                 , string?             beforeId
                                                 , string?             afterId
                                                 , CancellationToken   cancellationToken)
    {
        var beforeRank = await GetNeighborRankAsync(connection, column, beforeId, movingStoryId, cancellationToken);
        var afterRank  = await GetNeighborRankAsync(connection, column, afterId, movingStoryId, cancellationToken);

        if (beforeRank is not null && afterRank is not null && afterRank - beforeRank > 1)
        {
            return ((beforeRank.Value + afterRank.Value) / 2).ToString("D20");
        }

        if (afterRank is not null && beforeRank is null && afterRank > 1)
        {
            return (afterRank.Value / 2).ToString("D20");
        }

        if (beforeRank is not null && afterRank is null)
        {
            return (beforeRank.Value + RankStep).ToString("D20");
        }

        if (beforeRank is null && afterRank is null)
        {
            return await GetAppendRankAsync(connection, column, cancellationToken);
        }

        await RebalanceRanksAsync(connection, column, movingStoryId, cancellationToken);
        beforeRank = await GetNeighborRankAsync(connection, column, beforeId, movingStoryId, cancellationToken);
        afterRank  = await GetNeighborRankAsync(connection, column, afterId, movingStoryId, cancellationToken);

        if (afterRank is not null && beforeRank is null)
        {
            return (afterRank.Value / 2).ToString("D20");
        }

        if (beforeRank is not null && afterRank is not null)
        {
            return ((beforeRank.Value + afterRank.Value) / 2).ToString("D20");
        }

        return beforeRank is not null
            ? (beforeRank.Value + RankStep).ToString("D20")
            : await GetAppendRankAsync(connection, column, cancellationToken);
    }

    private async Task<long?> GetNeighborRankAsync( SqliteConnection  connection
                                                   , string            column
                                                   , string?           storyId
                                                   , Guid              movingStoryId
                                                   , CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(storyId, out var parsedStoryId) || parsedStoryId == movingStoryId)
        {
            return null;
        }

        var story = await GetStoryAsync(connection, parsedStoryId, cancellationToken);
        return story is not null && story.ColumnKey.Equals(column, StringComparison.OrdinalIgnoreCase)
            ? long.Parse(story.Rank)
            : null;
    }

    private static async Task RebalanceRanksAsync( SqliteConnection  connection
                                                  , string            column
                                                  , Guid              excludedStoryId
                                                  , CancellationToken cancellationToken)
    {
        var stories = new List<(string Id, long Rank)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id FROM BacklogStories WHERE ColumnKey = $column AND IsArchived = 0 AND Id <> $excludedStoryId ORDER BY Rank, DisplayId;";
            select.Parameters.AddWithValue("$column", column.Trim());
            select.Parameters.AddWithValue("$excludedStoryId", excludedStoryId.ToString());
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            var nextRank = RankStep;
            while (await reader.ReadAsync(cancellationToken))
            {
                stories.Add((reader.GetString(0), nextRank));
                nextRank += RankStep;
            }
        }

        foreach (var story in stories)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE BacklogStories SET Rank = $rank WHERE Id = $id;";
            update.Parameters.AddWithValue("$rank", story.Rank.ToString("D20"));
            update.Parameters.AddWithValue("$id", story.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }
    private async Task<BacklogStoryDto?> GetStoryAsync(SqliteConnection connection, Guid id, bool includeArchived, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = $"SELECT Id, DisplayId, Title, Description, ProjectKey, AreaKey, StreamKey, ItemTypeKey, ColumnKey, Rank, Priority, Revision, PropertiesJson FROM BacklogStories WHERE Id = $id {(includeArchived ? string.Empty : "AND IsArchived = 0")};"; command.Parameters.AddWithValue("$id", id.ToString()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadStory(reader) : null; }
    private Task<BacklogStoryDto?> GetStoryAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken) => GetStoryAsync(connection, id, includeArchived: false, cancellationToken);
    private static async Task SetStoryArchivedAsync(SqliteConnection connection, Guid id, bool isArchived, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "UPDATE BacklogStories SET IsArchived = $isArchived WHERE Id = $id;"; command.Parameters.AddWithValue("$isArchived", isArchived ? 1 : 0); command.Parameters.AddWithValue("$id", id.ToString()); await command.ExecuteNonQueryAsync(cancellationToken); }
    private static async Task<bool> IsStoryArchivedAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT IsArchived FROM BacklogStories WHERE Id = $id;"; command.Parameters.AddWithValue("$id", id.ToString()); return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1; }
    private static async Task InsertStoryAsync(SqliteConnection connection, BacklogStoryDto story, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO BacklogStories (Id, DisplayId, Title, Description, ProjectKey, AreaKey, StreamKey, ItemTypeKey, ColumnKey, Rank, Priority, Revision, PropertiesJson, IsArchived) VALUES ($id, $displayId, $title, $description, $project, $area, $stream, $type, $column, $rank, $priority, $revision, $properties, 0);"; AddStoryParameters(command, story); await command.ExecuteNonQueryAsync(cancellationToken); }
    private static async Task UpdateStoryAsync(SqliteConnection connection, BacklogStoryDto story, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "UPDATE BacklogStories SET Title = $title, Description = $description, ProjectKey = $project, AreaKey = $area, StreamKey = $stream, ItemTypeKey = $type, ColumnKey = $column, Rank = $rank, Priority = $priority, Revision = $revision, PropertiesJson = $properties WHERE Id = $id;"; AddStoryParameters(command, story); await command.ExecuteNonQueryAsync(cancellationToken); }
    private static void AddStoryParameters(SqliteCommand command, BacklogStoryDto story) { command.Parameters.AddWithValue("$id", story.Id.ToString()); command.Parameters.AddWithValue("$displayId", story.DisplayId); command.Parameters.AddWithValue("$title", story.Title); command.Parameters.AddWithValue("$description", story.Description); command.Parameters.AddWithValue("$project", story.ProjectKey); command.Parameters.AddWithValue("$area", story.AreaKey); command.Parameters.AddWithValue("$stream", (object?)story.StreamKey ?? DBNull.Value); command.Parameters.AddWithValue("$type", story.ItemTypeKey); command.Parameters.AddWithValue("$column", story.ColumnKey); command.Parameters.AddWithValue("$rank", story.Rank); command.Parameters.AddWithValue("$priority", story.Priority); command.Parameters.AddWithValue("$revision", story.Revision); command.Parameters.AddWithValue("$properties", JsonSerializer.Serialize(story.Properties)); }
    private static async Task WriteAuditAsync(SqliteConnection connection, string entityType, string entityId, string action, string? actor, object? before, object? after, CancellationToken cancellationToken) { await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO BacklogAuditEvents (Id, EntityType, EntityId, Action, Actor, CorrelationId, OccurredAtUtc, BeforeJson, AfterJson) VALUES ($id, $entityType, $entityId, $action, $actor, $correlationId, $occurredAtUtc, $beforeJson, $afterJson);"; command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$entityType", entityType); command.Parameters.AddWithValue("$entityId", entityId); command.Parameters.AddWithValue("$action", action); command.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor); command.Parameters.AddWithValue("$correlationId", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$occurredAtUtc", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$beforeJson", ToJson(before)); command.Parameters.AddWithValue("$afterJson", ToJson(after)); await command.ExecuteNonQueryAsync(cancellationToken); }
    private static async Task SeedDefaultsAsync(SqliteConnection connection, CancellationToken cancellationToken) { foreach (var reference in Defaults) { await using var command = connection.CreateCommand(); command.CommandText = "INSERT OR IGNORE INTO BacklogReferences (ReferenceType, Key, Name, SortOrder, IsArchived) VALUES ($type, $key, $name, $sortOrder, 0);"; command.Parameters.AddWithValue("$type", reference.Type); command.Parameters.AddWithValue("$key", reference.Key); command.Parameters.AddWithValue("$name", reference.Name); command.Parameters.AddWithValue("$sortOrder", reference.SortOrder); await command.ExecuteNonQueryAsync(cancellationToken); } }

    private static readonly (string Type, string Key, string Name, int SortOrder)[] Defaults = [ ("project", "cognitive-platform", "Cognitive Platform", 10), ("area", "unassigned", "Unassigned", 10), ("column", "backlog", "Backlog", 10), ("column", "planned", "Planned", 20), ("column", "in-progress", "In Progress", 30), ("column", "done", "Done", 40), ("type", "story", "Story", 10), ("type", "bug", "Bug", 20), ("type", "enhancement", "Enhancement", 30), ("type", "technical-debt", "Technical Debt", 40), ("type", "ux", "UX / UI", 50), ("property", "release", "Release", 10), ("property", "owner", "Owner", 20), ("property", "estimate", "Estimate", 30), ("property", "dependency", "Dependency", 40), ("property", "customer-impact", "Customer Impact", 50) ];

    private const string SchemaSql = """
        PRAGMA foreign_keys = ON;
        CREATE TABLE IF NOT EXISTS BacklogReferences (ReferenceType TEXT NOT NULL, Key TEXT NOT NULL, Name TEXT NOT NULL, SortOrder INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (ReferenceType, Key));
        CREATE TABLE IF NOT EXISTS BacklogStories (Id TEXT PRIMARY KEY, DisplayId TEXT NOT NULL UNIQUE, Title TEXT NOT NULL, Description TEXT NOT NULL, ProjectKey TEXT NOT NULL, AreaKey TEXT NOT NULL, StreamKey TEXT NULL, ItemTypeKey TEXT NOT NULL, ColumnKey TEXT NOT NULL, Rank TEXT NOT NULL, Priority INTEGER NOT NULL, Revision INTEGER NOT NULL, PropertiesJson TEXT NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX IF NOT EXISTS IX_BacklogStories_ColumnRank ON BacklogStories (ColumnKey, Rank);
        CREATE TABLE IF NOT EXISTS BacklogIntake (Id TEXT PRIMARY KEY, Kind TEXT NOT NULL, Description TEXT NOT NULL, Source TEXT NOT NULL, SubmittedAtUtc TEXT NOT NULL, State TEXT NOT NULL, PromotedStoryId TEXT NULL);
        CREATE TABLE IF NOT EXISTS BacklogAuditEvents (Id TEXT PRIMARY KEY, EntityType TEXT NOT NULL, EntityId TEXT NOT NULL, Action TEXT NOT NULL, Actor TEXT NOT NULL, CorrelationId TEXT NOT NULL, OccurredAtUtc TEXT NOT NULL, BeforeJson TEXT NOT NULL, AfterJson TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS IX_BacklogAuditEvents_Entity ON BacklogAuditEvents (EntityType, EntityId, OccurredAtUtc DESC);
        """;

    private const long RankStep = 1_000_000;
}
