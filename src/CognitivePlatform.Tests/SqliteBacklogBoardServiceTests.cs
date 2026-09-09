using CognitivePlatform.Api.Domains.Backlog;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

public sealed class SqliteBacklogBoardServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"backlog-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task CreateStoryAsync_StoresTypedPropertiesAndAuditEvent()
    {
        var service = CreateService();
        await service.CreateReferenceAsync("area", new CreateBacklogReferenceRequest("admin", "CP Admin", 20));

        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest(
            "cognitive-platform"
          , "admin"
          , "story"
          , "backlog"
          , "Backlog redesign"
          , "Move planning data into the backlog domain."
          , 10
          , null
          , new Dictionary<string, string>
            {
                ["release"]         = "Next"
              , ["owner"]           = "Ben"
              , ["estimate"]        = "8"
              , ["dependency"]      = "None"
              , ["customer-impact"] = "High"
            }));

        var board = await service.GetBoardAsync();
        var history = await service.GetHistoryAsync(story.Id);

        Assert.Single(board.Stories);
        Assert.Equal("Ben", board.Stories[0].Properties["owner"]);
        Assert.Single(history);
        Assert.Equal("created", history[0].Action);
    }

    [Fact]
    public async Task MoveStoryAsync_ChangesColumnAndRevision()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Title", "Description", 50, null, null));

        var moved = await service.MoveStoryAsync(story.Id, new MoveBacklogStoryRequest("planned", null, null, story.Revision));

        Assert.Equal("planned", moved.ColumnKey);
        Assert.Equal(story.Revision + 1, moved.Revision);
    }

    [Fact]
    public async Task MoveStoryAsync_AllowsMoveIntoEmptyInProgressColumn()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Title", "Description", 50, null, null));

        var moved = await service.MoveStoryAsync(story.Id, new MoveBacklogStoryRequest("in-progress", null, null, story.Revision));

        Assert.Equal("in-progress", moved.ColumnKey);
    }

    [Fact]
    public async Task ArchiveStoryAsync_HidesDoneStoryAndAllowsUnarchive()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "done", "Completed", "Archive me", 50, null, null));

        await service.ArchiveStoryAsync(story.Id, new ArchiveBacklogStoryRequest(story.Revision));

        Assert.Empty((await service.GetBoardAsync()).Stories);
        Assert.Equal(story.Id, Assert.Single(await service.GetArchivedStoriesAsync()).Id);
        await service.UnarchiveStoryAsync(story.Id);
        Assert.Equal(story.Id, Assert.Single((await service.GetBoardAsync()).Stories).Id);
    }

    [Fact]
    public async Task ArchiveStoryAsync_RejectsNonDoneStories()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Active", "Do not archive", 50, null, null));

        await Assert.ThrowsAsync<BacklogValidationException>(() => service.ArchiveStoryAsync(story.Id, new ArchiveBacklogStoryRequest(story.Revision)));
    }

    [Fact]
    public async Task MoveStoryAsync_PlacesStoryBeforeSpecifiedNeighbor()
    {
        var service = CreateService();
        var first = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "First", "First story", 50, null, null));
        var second = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Second", "Second story", 50, null, null));
        var third = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Third", "Third story", 50, null, null));

        await service.MoveStoryAsync(third.Id, new MoveBacklogStoryRequest("backlog", null, first.Id.ToString(), third.Revision));

        var orderedTitles = (await service.GetBoardAsync()).Stories
                                                   .Where(story => story.ColumnKey == "backlog")
                                                   .OrderBy(story => story.Rank)
                                                   .Select(story => story.Title)
                                                   .ToList();
        Assert.Equal(new[] { "Third", "First", "Second" }, orderedTitles);
    }

    [Fact]
    public async Task UpdateReferenceAsync_RenamesAndReordersAStream()
    {
        var service = CreateService();
        await service.CreateReferenceAsync("stream", new CreateBacklogReferenceRequest("platform", "Platform work", 30));

        var updated = await service.UpdateReferenceAsync("stream", "platform", new UpdateBacklogReferenceRequest("Platform foundation", 5, false));

        var stream = Assert.Single((await service.GetBoardAsync()).Streams);
        Assert.Equal("platform", updated.Key);
        Assert.Equal("Platform foundation", stream.Name);
        Assert.Equal(5, stream.SortOrder);
        Assert.False(stream.IsArchived);
    }

    [Fact]
    public async Task ResetAsync_RemovesStoriesAndLeavesDefaultDefinitions()
    {
        var service = CreateService();
        await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Temporary", "Testing restore", 50, null, null));
        var backup = await service.CreateBackupAsync();

        try
        {
            await service.ResetAsync(backup);

            var board = await service.GetBoardAsync();
            Assert.Empty(board.Stories);
            Assert.Contains(board.Projects, project => project.Key == "cognitive-platform");
            Assert.Contains(board.Columns, column => column.Key == "backlog");
        }
        finally
        {
            if (File.Exists(backup.Path)) File.Delete(backup.Path);
        }
    }

    [Fact]
    public async Task CreateBackupAsync_WritesRecoverableSnapshot()
    {
        var service = CreateService();
        await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Before backup", "Backup test", 50, null, null));

        var backup = await service.CreateBackupAsync();

        try
        {
            Assert.True(File.Exists(backup.Path));
            await using var connection = new SqliteConnection($"Data Source={backup.Path};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM BacklogStories;";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            if (File.Exists(backup.Path)) File.Delete(backup.Path);
        }
    }

    [Fact]
    public async Task ResetAsync_RejectsAnUnverifiedBackup()
    {
        var service = CreateService();
        var backup = new BacklogRecoveryBackup(Path.Combine(Path.GetTempPath(), "missing-backup.db"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<BacklogValidationException>(() => service.ResetAsync(backup));
    }

    [Fact]
    public async Task RestoreBackupAsync_RestoresStoriesAfterReset()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Recover me", "Test recovery", 50, null, null));
        var backup = await service.CreateBackupAsync();

        try
        {
            await service.ResetAsync(backup);
            await service.RestoreBackupAsync(backup);

            Assert.Equal(story.Id, Assert.Single((await service.GetBoardAsync()).Stories).Id);
        }
        finally
        {
            if (File.Exists(backup.Path)) File.Delete(backup.Path);
        }
    }

    [Fact]
    public async Task ExportMarkdownAsync_IncludesDescriptionMetadataAndCustomProperties()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest(
            "cognitive-platform", "unassigned", "story", "backlog", "Export | title", "First line\nSecond | line", 7, null,
            new Dictionary<string, string> { ["owner"] = "Ben", ["acceptance-notes"] = "Ready | after review" }));

        var markdown = await service.ExportMarkdownAsync();

        Assert.Contains($"### {story.DisplayId} — Export | title", markdown);
        Assert.Contains("First line", markdown);
        Assert.Contains("Second | line", markdown);
        Assert.Contains("| owner | Ben |", markdown);
        Assert.Contains("| acceptance-notes | Ready \\| after review |", markdown);
        Assert.Contains("| Rank |", markdown);
    }

    [Fact]
    public async Task ImportAsync_ImportsLegacyRowsWithoutChangingSource()
    {
        var service = CreateService();
        var sourcePath = Path.Combine(Path.GetTempPath(), $"legacy-backlog-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(sourcePath, """
            # Legacy

            ## Enhancements
            | ID | Description | Area | Status |
            | --- | --- | --- | --- |
            | ENH-42 | **Portable backlog** — Make storage reliable. <!-- priority: 12 --> | CP Admin | Planned |
            """);

        try
        {
            var report = await new LegacyBacklogImporter(service).ImportAsync("cognitive-platform", sourcePath);
            var board = await service.GetBoardAsync();

            Assert.True(report.IsReconciled);
            Assert.Equal(1, report.ImportedRows);
            Assert.Single(board.Stories);
            Assert.Equal("ENH-42", board.Stories[0].DisplayId);
            Assert.Equal("planned", board.Stories[0].ColumnKey);
            Assert.Equal(12, board.Stories[0].Priority);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ImportAsync_PreservesPipesInsideMarkdownDescriptions()
    {
        var service = CreateService();
        var sourcePath = Path.Combine(Path.GetTempPath(), $"legacy-backlog-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(sourcePath, """
            ## UX / UI Issues
            | ID | Description | Area | Status |
            | --- | --- | --- | --- |
            | UX-42 | **Clarify transfer** — Needs a decision [Scope | Prerequisite | Detail]. | CP API / Chat | Open |
            """);

        try
        {
            var report = await new LegacyBacklogImporter(service).ImportAsync("cognitive-platform", sourcePath);
            var story = Assert.Single((await service.GetBoardAsync()).Stories);

            Assert.True(report.IsReconciled);
            Assert.Equal("cp-api-chat", story.AreaKey);
            Assert.Equal("Open", story.Properties["legacy-status"]);
            Assert.Contains("Scope | Prerequisite | Detail", story.Description);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ImportAsync_ImportsArchivedCognitivePlatformLegacySnapshotIntoAnIsolatedDatabase()
    {
        const string sourcePath = @"C:\Users\benho\source\Application Documentation\The CP Universe\Documentation\Archive\Backlog Board Legacy\2026-09-09\BACKLOG.original.md";
        var sourceBefore = await File.ReadAllBytesAsync(sourcePath);

        var report = await new LegacyBacklogImporter(CreateService()).ImportAsync("cognitive-platform", sourcePath);

        Assert.True(report.SourceRows > 0);
        Assert.True(report.IsReconciled, string.Join(Environment.NewLine, report.Exceptions));
        Assert.Equal(report.SourceRows, report.ImportedRows + report.SkippedRows);
        Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(sourcePath));
    }

    [Fact]
    public async Task PreviewAsync_ReportsMetadataDifferencesWithoutChangingBoard()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Legacy story", "Old detail", 50, null, new Dictionary<string, string> { ["legacy-id"] = "STORY-42" }));
        var sourcePath = await WriteLegacySourceAsync("""
            ## Enhancements
            | ID | Description | Area | Status |
            | --- | --- | --- |
            | STORY-42 | **Legacy story** — Updated detail <!-- priority: 12 --> [Stream Alpha] | Unassigned | Planned |
            """);

        try
        {
            var preview = await new LegacyBacklogReconciliationService(service, sourcePath).PreviewAsync();

            var difference = Assert.Single(preview.Stories);
            Assert.Equal(story.Id, difference.StoryId);
            Assert.Equal(12, difference.LegacyPriority);
            Assert.Equal("Stream Alpha", difference.LegacyStreamName);
            Assert.Equal("planned", difference.LegacyColumnKey);
            Assert.True(difference.DescriptionDiffers);
            Assert.Equal(50, Assert.Single((await service.GetBoardAsync()).Stories).Priority);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_AppliesSelectedMetadataAndCreatesMissingStream()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Legacy story", "Detail", 50, null, new Dictionary<string, string> { ["legacy-id"] = "STORY-43" }));
        var sourcePath = await WriteLegacySourceAsync("""
            ## Enhancements
            | ID | Description | Area | Status |
            | --- | --- | --- |
            | STORY-43 | **Legacy story** — Detail <!-- priority: 12 --> [Stream Alpha] | Unassigned | Planned |
            """);

        try
        {
            var reconciler = new LegacyBacklogReconciliationService(service, sourcePath);
            var preview = await reconciler.PreviewAsync();
            var result = await reconciler.ApplyAsync(new LegacyReconciliationApplyRequest(preview.SourceFingerprint,
                [new LegacyReconciliationSelection(story.Id, story.Revision, true, true, true)]));
            var updated = Assert.Single((await service.GetBoardAsync()).Stories);

            Assert.Equal("applied", Assert.Single(result.Outcomes).Outcome);
            Assert.Equal(12, updated.Priority);
            Assert.Equal("stream-alpha", updated.StreamKey);
            Assert.Equal("planned", updated.ColumnKey);
            Assert.Contains((await service.GetBoardAsync()).Streams, stream => stream.Key == "stream-alpha");
            Assert.Equal("Detail", updated.Description);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_RejectsPreviewWhenLegacySourceChanges()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Legacy story", "Detail", 50, null, new Dictionary<string, string> { ["legacy-id"] = "STORY-44" }));
        var sourcePath = await WriteLegacySourceAsync("""
            ## Enhancements
            | ID | Description | Area | Status |
            | --- | --- | --- |
            | STORY-44 | **Legacy story** — Detail <!-- priority: 12 --> | Unassigned | Planned |
            """);

        try
        {
            var reconciler = new LegacyBacklogReconciliationService(service, sourcePath);
            var preview = await reconciler.PreviewAsync();
            await File.AppendAllTextAsync(sourcePath, Environment.NewLine);

            await Assert.ThrowsAsync<BacklogConflictException>(() => reconciler.ApplyAsync(new LegacyReconciliationApplyRequest(preview.SourceFingerprint,
                [new LegacyReconciliationSelection(story.Id, story.Revision, true, false, false)])));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_SkipsStoryWhenRevisionChangedAfterPreview()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "backlog", "Legacy story", "Detail", 50, null, new Dictionary<string, string> { ["legacy-id"] = "STORY-45" }));
        var sourcePath = await WriteLegacySourceAsync("""
            ## Enhancements
            | ID | Description | Area | Status |
            | --- | --- | --- |
            | STORY-45 | **Legacy story** — Detail <!-- priority: 12 --> | Unassigned | Planned |
            """);

        try
        {
            var reconciler = new LegacyBacklogReconciliationService(service, sourcePath);
            var preview = await reconciler.PreviewAsync();
            await service.UpdateStoryAsync(story.Id, new UpdateBacklogStoryRequest("Legacy story", "Changed after preview", "cognitive-platform", "unassigned", "story", 50, null, story.Properties, story.Revision));

            var result = await reconciler.ApplyAsync(new LegacyReconciliationApplyRequest(preview.SourceFingerprint,
                [new LegacyReconciliationSelection(story.Id, story.Revision, true, false, true)]));

            Assert.Equal("skipped", Assert.Single(result.Outcomes).Outcome);
            Assert.Equal(50, Assert.Single((await service.GetBoardAsync()).Stories).Priority);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task PreviewAsync_ResolvesLegacyStreamPrefixToExistingBoardStream()
    {
        var service = CreateService();
        await service.CreateReferenceAsync("stream", new CreateBacklogReferenceRequest("b", "Stream B — LAA Chat UX", 10));
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "ux", "done", "Legacy story", "Detail", 22, "b", new Dictionary<string, string> { ["legacy-id"] = "UX-42" }));
        var sourcePath = await WriteLegacySourceAsync("""
            ## UX / UI Issues
            | ID | Description | Area | Status |
            | --- | --- | --- |
            | UX-42 | **Legacy story** — Detail <!-- priority: 12 --> [Stream B] | Unassigned | Fixed |
            """);

        try
        {
            var preview = await new LegacyBacklogReconciliationService(service, sourcePath).PreviewAsync();

            var difference = Assert.Single(preview.Stories);
            Assert.False(difference.StreamDiffers);
            Assert.Equal(story.Id, difference.StoryId);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task ApplyAsync_AppliesSelectedTitleAndDescriptionIndependently()
    {
        var service = CreateService();
        var story = await service.CreateStoryAsync(new CreateBacklogStoryRequest("cognitive-platform", "unassigned", "story", "planned", "Board title", "Board detail", 50, null, new Dictionary<string, string> { ["legacy-id"] = "STORY-46" }));
        var sourcePath = await WriteLegacySourceAsync("""
            ## Enhancements
            | ID | Description | Area | Status |
            | --- | --- | --- |
            | STORY-46 | **Legacy title** — Legacy detail <!-- priority: 50 --> `[Stream Alpha]` | Unassigned | Planned |
            """);

        try
        {
            var reconciler = new LegacyBacklogReconciliationService(service, sourcePath);
            var titlePreview = await reconciler.PreviewAsync();
            var titleResult = await reconciler.ApplyAsync(new LegacyReconciliationApplyRequest(titlePreview.SourceFingerprint,
                [new LegacyReconciliationSelection(story.Id, story.Revision, false, false, false, true, false)]));
            var titleUpdated = Assert.Single((await service.GetBoardAsync()).Stories);

            Assert.Equal("applied", Assert.Single(titleResult.Outcomes).Outcome);
            Assert.Equal("Legacy title", titleUpdated.Title);
            Assert.Equal("Board detail", titleUpdated.Description);

            var descriptionPreview = await reconciler.PreviewAsync();
            var descriptionResult = await reconciler.ApplyAsync(new LegacyReconciliationApplyRequest(descriptionPreview.SourceFingerprint,
                [new LegacyReconciliationSelection(titleUpdated.Id, titleUpdated.Revision, false, false, false, false, true)]));
            var descriptionUpdated = Assert.Single((await service.GetBoardAsync()).Stories);

            Assert.Equal("applied", Assert.Single(descriptionResult.Outcomes).Outcome);
            Assert.Equal("Legacy title", descriptionUpdated.Title);
            Assert.Equal("Legacy detail", descriptionUpdated.Description);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private SqliteBacklogBoardService CreateService()
    {
        return new SqliteBacklogBoardService($"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Shared;Pooling=False");
    }

    private static async Task<string> WriteLegacySourceAsync(string contents)
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"legacy-backlog-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(sourcePath, contents);
        return sourcePath;
    }
}
