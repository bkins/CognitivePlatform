using System.Security.Cryptography;
using CognitivePlatform.Api.Domains.Journal.Import.Ttr;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class TtrImportPlannerTests : IDisposable
{
    private readonly string _root;
    private readonly string _databasePath;

    public TtrImportPlannerTests()
    {
        _root         = Path.Combine(Path.GetTempPath(), $"cp-ttr-plan-{Guid.NewGuid():N}");
        _databasePath = Path.Combine(_root, "fixture.db3");
        Directory.CreateDirectory(_root);
        CreateFixture();
    }

    [Fact]
    public async Task PlanAsync_ValidFixture_ProducesDeterministicPlanWithoutWrites()
    {
        var databaseHash       = await ComputeSha256Async(_databasePath);
        var databaseWriteTime  = File.GetLastWriteTimeUtc(_databasePath);
        var filesBefore        = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
        var planner            = new TtrImportPlanner(new TtrContentNormalizer());
        var request            = CreateRequest(databaseHash);

        var first  = await planner.PlanAsync(request);
        var second = await planner.PlanAsync(request);

        Assert.Equal(databaseHash, first.SourceSha256);
        Assert.Equal(first.PlanSha256, second.PlanSha256);
        Assert.Equal(2, first.Entries.Count);
        Assert.Equal(2, first.Media.Count);
        Assert.Equal(2, first.ReadyEntryCount);
        Assert.Equal(0, first.BlockedEntryCount);
        Assert.Equal(filesBefore, Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Equal(databaseWriteTime, File.GetLastWriteTimeUtc(_databasePath));
        Assert.All(first.Entries, entry => Assert.Equal("Ready", entry.Disposition));
        Assert.Contains(first.IgnoredFiles, file => file.EndsWith("Test 2_19.mp4", StringComparison.Ordinal));
        Assert.Contains(first.PlanSha256, first.HumanReadableReport, StringComparison.Ordinal);
        Assert.Contains("Entries: 2 total, 2 ready, 0 blocked", first.HumanReadableReport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_ValidFixture_MapsHistoricalContentAndMetadata()
    {
        var databaseHash = await ComputeSha256Async(_databasePath);
        var planner      = new TtrImportPlanner(new TtrContentNormalizer());

        var plan = await planner.PlanAsync(CreateRequest(databaseHash));

        var entry = Assert.Single(plan.Entries, item => item.SourceEntryId == 1);
        Assert.Equal("# A title\n\nHello **world**\n\n- One", entry.NormalizedText);
        Assert.Equal(new DateTimeOffset(2021, 1, 2, 3, 4, 5, TimeSpan.FromHours(-8)), entry.CreatedUtc);
        Assert.Contains("source:ttr", entry.Tags);
        Assert.Contains("journal:daily-notes", entry.Tags);
        Assert.Contains("journal-type:personal", entry.Tags);
        Assert.Equal("Good 🙂", entry.Mood);
        Assert.Null(entry.MoodScore);
        Assert.Null(entry.MoodLevel);
        Assert.True(Guid.TryParse(entry.EntryId, out _));
        Assert.True(Guid.TryParse(entry.RevisionId, out _));
    }

    [Fact]
    public async Task PlanAsync_MissingVideo_BlocksOwningEntryWithExactFinding()
    {
        File.Delete(Path.Combine(_root, "clip.mp4"));
        var databaseHash = await ComputeSha256Async(_databasePath);
        var planner      = new TtrImportPlanner(new TtrContentNormalizer());

        var plan = await planner.PlanAsync(CreateRequest(databaseHash));

        var entry = Assert.Single(plan.Entries, item => item.SourceEntryId == 2);
        Assert.Equal("Blocked", entry.Disposition);
        Assert.Contains(plan.Findings, finding => finding.Code == "media-file-missing"
                                               && finding.SourceEntryId == 2
                                               && finding.IsBlocking);
    }

    [Fact]
    public async Task PlanAsync_ChangedDatabaseHash_BlocksBeforeReadingRows()
    {
        var planner = new TtrImportPlanner(new TtrContentNormalizer());
        var request = CreateRequest(new string('0', 64));

        var exception = await Assert.ThrowsAsync<TtrImportValidationException>(() => planner.PlanAsync(request));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_HtmlTitleAndInlineImage_ProducesSafeMarkdownAndExtractedMedia()
    {
        var normalizer = new TtrContentNormalizer();
        var body       = "<p>Hello <strong>world</strong></p><script>alert(1)</script><a href=\"javascript:alert(2)\">unsafe</a><img src=\"data:image/png;base64,iVBORw0KGgo=\"/><ul><li>One</li></ul>";

        var result = normalizer.Normalize("A title", body);

        Assert.Equal("# A title\n\nHello **world**\nunsafe\n- One", result.Markdown);
        Assert.DoesNotContain("javascript", result.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsafe-link-target-removed", result.Warnings);
        Assert.Contains("dangerous-html-removed", result.Warnings);
        Assert.DoesNotContain("script", result.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result.Markdown, StringComparison.OrdinalIgnoreCase);
        var image = Assert.Single(result.InlineMedia);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(Convert.FromBase64String("iVBORw0KGgo="), image.Bytes);
    }

    [Fact]
    public void CreateUuid5_SameName_IsStableAndDifferentKindChangesId()
    {
        var first     = TtrDeterministicIdentity.CreateEntryId("fixture", 42);
        var repeated  = TtrDeterministicIdentity.CreateEntryId("fixture", 42);
        var revision  = TtrDeterministicIdentity.CreateInitialRevisionId("fixture", 42);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, revision);
        Assert.Equal(5, first.ToByteArray()[7] >> 4);
    }

    [Theory]
    [InlineData("personal", null)]
    [InlineData("work", "work")]
    public async Task ExecuteAsync_ValidPlan_ImportsHistoricalDataAndRerunCreatesNoDuplicates(string targetWorkspace, string? storagePartition)
    {
        var sourceHash = await ComputeSha256Async(_databasePath);
        var planner    = new TtrImportPlanner(new TtrContentNormalizer());
        var request    = CreateRequest(sourceHash, targetWorkspace);
        var plan       = await planner.PlanAsync(request);
        var manifest   = Path.Combine(_root, "backup-manifest.sha256");
        await File.WriteAllTextAsync(manifest, "synthetic-backup");
        var manifestHash = await ComputeSha256Async(manifest);
        var targetPath   = Path.Combine(_root, "target.db");
        var store        = new SqliteObjectStore($"Data Source={targetPath};Pooling=False");
        var mediaService = new MediaAttachmentService(store
                                                    , new LocalMediaFileStorage()
                                                    , Options.Create(new MediaAttachmentSettings { MediaRootPath = Path.Combine(_root, "target-media") })
                                                    , NullLogger<MediaAttachmentService>.Instance);
        var executor = new TtrImportExecutor(store
                                           , new HistoricalJournalWriter(store, store)
                                           , mediaService
                                           , new TtrMediaContentReader(new TtrContentNormalizer())
                                           , "Test"
                                           , targetPath);
        var execution = new TtrImportExecutionRequest
                        {
                            ReviewedPlanSha256   = plan.PlanSha256
                          , BackupManifestPath   = manifest
                          , BackupManifestSha256 = manifestHash
                          , TargetEnvironment     = "Test"
                          , TargetDatabasePath    = targetPath
                        };

        var first  = await executor.ExecuteAsync(request, plan, execution);
        var replay = await executor.ExecuteAsync(request, plan, execution);

        Assert.Equal(2, first.ImportedCount);
        Assert.Equal(2, replay.AlreadyImportedCount);
        Assert.True(first.VerificationPassed);
        Assert.True(replay.VerificationPassed);
        Assert.Equal(2, store.List<JournalEntry>(storagePartition).Count);
        Assert.Equal(2, store.List<JournalRevision>(storagePartition).Count);
        Assert.Empty(store.List<JournalEntry>("personal"));
        Assert.Equal(2, store.List<MediaAttachment>().Count);
        Assert.All(store.List<JournalRevision>(storagePartition), revision => Assert.Equal(JournalEntryState.Committed, revision.State));
        Assert.Single(store.List<JournalEntry>(storagePartition, new DateTimeOffset(2021, 1, 2, 11, 0, 0, TimeSpan.Zero), new DateTimeOffset(2021, 1, 2, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task ExecuteAsync_MediaFailureThenResume_CompletesWithoutDuplicates()
    {
        var sourceHash   = await ComputeSha256Async(_databasePath);
        var normalizer   = new TtrContentNormalizer();
        var planner      = new TtrImportPlanner(normalizer);
        var source       = CreateRequest(sourceHash);
        var plan         = await planner.PlanAsync(source);
        var manifest     = Path.Combine(_root, "resume-backup-manifest.sha256");
        await File.WriteAllTextAsync(manifest, "synthetic-backup");
        var manifestHash = await ComputeSha256Async(manifest);
        var targetPath   = Path.Combine(_root, "resume-target.db");
        var store        = new SqliteObjectStore($"Data Source={targetPath};Pooling=False");
        var mediaService = new MediaAttachmentService(store
                                                    , new LocalMediaFileStorage()
                                                    , Options.Create(new MediaAttachmentSettings { MediaRootPath = Path.Combine(_root, "resume-media") })
                                                    , NullLogger<MediaAttachmentService>.Instance);
        var realReader = new TtrMediaContentReader(normalizer);
        var failOnce   = true;
        var reader     = new Mock<ITtrMediaContentReader>();
        reader.Setup(sourceReader => sourceReader.ReadAsync(It.IsAny<TtrImportPlanRequest>(), It.IsAny<TtrPlannedMedia>(), It.IsAny<CancellationToken>()))
              .Returns((TtrImportPlanRequest readSource, TtrPlannedMedia media, CancellationToken cancellationToken) =>
              {
                  if (media.SourceEntryId == 2 && failOnce)
                  {
                      failOnce = false;
                      throw new IOException("Synthetic interruption.");
                  }
                  return realReader.ReadAsync(readSource, media, cancellationToken);
              });
        var executor = new TtrImportExecutor(store
                                           , new HistoricalJournalWriter(store, store)
                                           , mediaService
                                           , reader.Object
                                           , "Test"
                                           , targetPath);
        var execution = new TtrImportExecutionRequest
                        {
                            ReviewedPlanSha256   = plan.PlanSha256
                          , BackupManifestPath   = manifest
                          , BackupManifestSha256 = manifestHash
                          , TargetEnvironment     = "Test"
                          , TargetDatabasePath    = targetPath
                        };

        var interrupted = await executor.ExecuteAsync(source, plan, execution);
        var resumed     = await executor.ExecuteAsync(source, plan, execution);

        Assert.Equal(1, interrupted.ImportedCount);
        Assert.Equal(1, interrupted.FailedCount);
        Assert.Equal(1, resumed.ImportedCount);
        Assert.Equal(1, resumed.AlreadyImportedCount);
        Assert.Equal(0, resumed.FailedCount);
        Assert.True(resumed.VerificationPassed);
        Assert.Equal(2, store.List<JournalEntry>(null).Count);
        Assert.Equal(2, store.List<MediaAttachment>().Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TtrImportPlanRequest CreateRequest(string expectedHash, string targetWorkspace = "personal")
    {
        return new TtrImportPlanRequest
               {
                   DatabasePath            = _databasePath
                 , RecoveryBundlePath      = _root
                 , ExpectedDatabaseSha256  = expectedHash
                 , LogicalSourceInstance   = "fixture"
                 , SourceTimeZoneId        = "America/Los_Angeles"
                 , TargetPartition         = targetWorkspace
               };
    }

    private void CreateFixture()
    {
        var connectionString = new SqliteConnectionStringBuilder
                               {
                                   DataSource = _databasePath
                                 , Pooling    = false
                               }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Mood (Id INTEGER PRIMARY KEY, Title TEXT, Emoji TEXT);
            CREATE TABLE Journal (Id INTEGER PRIMARY KEY, Title TEXT, JournalTypeId INTEGER);
            CREATE TABLE Entry (Id INTEGER PRIMARY KEY, Title TEXT, Text TEXT, CreateDateTime INTEGER, JournalId INTEGER, MoodId INTEGER, Image BLOB, ImageFileName TEXT, Video BLOB, VideoFileName TEXT, OriginalJournalId INTEGER);
            CREATE TABLE JournalType (Id INTEGER PRIMARY KEY, Title TEXT);
            CREATE TABLE Media (Id INTEGER PRIMARY KEY, MediaBytes BLOB, MediaFileName TEXT, Type INTEGER, EntryId INTEGER);
            CREATE TABLE Notification (Id INTEGER PRIMARY KEY, IntentId INTEGER, Scheduled INTEGER, Interval INTEGER, Type INTEGER, TypeId INTEGER, Title TEXT, Message TEXT, Description TEXT, IntervalType INTEGER, TriggerDateTime INTEGER, DynamicNotificationName TEXT, DynamicType INTEGER, SystemOnly INTEGER, ScheduledVerified INTEGER);
            INSERT INTO Mood VALUES (1, 'Good', '🙂');
            INSERT INTO JournalType VALUES (1, 'Personal');
            INSERT INTO Journal VALUES (1, 'Daily Notes', 1);
            INSERT INTO Entry VALUES (1, 'A title', '<p>Hello <strong>world</strong></p><ul><li>One</li></ul>', 637451534450000000, 1, 1, NULL, NULL, NULL, NULL, 0);
            INSERT INTO Entry VALUES (2, NULL, 'Video entry', 637452398450000000, 1, 1, NULL, NULL, NULL, NULL, 0);
            INSERT INTO Media VALUES (1, X'89504E470D0A1A0A', 'image.png', 0, 1);
            INSERT INTO Media VALUES (2, NULL, '/data/user/0/com.companyname.thingstoremember/files/clip.mp4', 1, 2);
            """;
        command.ExecuteNonQuery();

        File.WriteAllBytes(Path.Combine(_root, "clip.mp4"), [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D]);
        File.WriteAllBytes(Path.Combine(_root, "Test 2_19.mp4"), [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}
