using CognitivePlatform.Api.Controllers.Admin;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public class AdminJournalControllerTests : IDisposable
{
    private const string AdminSecret = "test-admin-secret";

    private readonly SqliteConnection                  _persistentConnection;
    private readonly SqliteObjectStore                 _store;
    private readonly Mock<IJournalRevisionRepository> _revisionsMock = new();
    private readonly AdminJournalController            _sut;

    public AdminJournalControllerTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source=file:{dbName}?mode=memory&cache=shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _store = new SqliteObjectStore(connectionString);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();

        _sut = new AdminJournalController(config, _store, _revisionsMock.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Secret"] = AdminSecret;
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    public void Dispose()
    {
        _persistentConnection.Dispose();
    }

    private AdminJournalController CreateUnauthenticated()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();

        var controller = new AdminJournalController(config, _store, _revisionsMock.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    // ── GetEntries ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetEntries_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();

        var result = controller.GetEntries();

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetEntries_Returns200_WithMappedEntriesAndExcerpts()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var entry   = new JournalEntry
                      {
                          Id         = entryId
                        , CreatedUtc = DateTimeOffset.UtcNow
                      };

        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = entryId
                         , CreatedUtc = DateTimeOffset.UtcNow
                         , Text       = "A long text excerpt for testing admin journal entry retrieval."
                       };

        await _store.Save(entry, id: entryId);
        await _store.Save(revision, id: revision.RevisionId);

        var result = _sut.GetEntries();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    // ── GetRevisions ───────────────────────────────────────────────────────────

    [Fact]
    public void GetRevisions_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();

        var result = controller.GetRevisions("some-entry-id");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    [Fact]
    public void GetRevisions_Returns200_WithRevisionsForEntry()
    {
        var entryId   = Guid.NewGuid().ToString("N");
        var revisions = new List<JournalRevision>
                        {
                            new()
                            {
                                RevisionId = Guid.NewGuid().ToString("N")
                              , EntryId    = entryId
                              , CreatedUtc = DateTimeOffset.UtcNow
                              , Text       = "Sample revision"
                              , Tags       = new List<string> { "tag1" }
                            }
                        };

        _revisionsMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                      .Returns(revisions);

        var result = _sut.GetRevisions(entryId);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    // ── AddCorrection ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddCorrection_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();
        var request    = new AddCorrectionRequest { Text = "Correction text" };

        var result = await controller.AddCorrection("some-entry-id", request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddCorrection_Returns400_WhenTextIsEmpty()
    {
        var request = new AddCorrectionRequest { Text = "" };

        var result = await _sut.AddCorrection("some-entry-id", request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Text is required.", badRequest.Value);
    }

    [Fact]
    public async Task AddCorrection_Returns404_WhenEntryNotFound()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var request = new AddCorrectionRequest { Text = "Correction text" };

        var result = await _sut.AddCorrection(entryId, request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal($"Journal entry '{entryId}' not found.", notFound.Value);
    }

    [Fact]
    public async Task AddCorrection_Returns200_WithSavedRevisionId_EvenIfSoftDeleted()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var request = new AddCorrectionRequest
                      {
                          Text      = "Admin correction"
                        , Tags      = new[] { "admin" }
                        , Mood      = "Neutral"
                        , MoodScore = 3
                      };

        var entry = new JournalEntry
                    {
                        Id         = entryId
                      , CreatedUtc = DateTimeOffset.UtcNow
                    };

        await _store.Save(entry, id: entryId);
        _store.SoftDelete<JournalEntry>(entryId);

        var result = await _sut.AddCorrection(entryId, request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    // ── CreateEntry ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEntry_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();
        var request    = new CreateJournalEntryAdminRequest { Text = "Test entry" };

        var result = await controller.CreateEntry(request);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    [Fact]
    public async Task CreateEntry_Returns400_WhenTextIsEmpty()
    {
        var request = new CreateJournalEntryAdminRequest { Text = "  " };

        var result = await _sut.CreateEntry(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Text is required.", badRequest.Value);
    }

    [Fact]
    public async Task CreateEntry_Returns200_AndPersistsEntryAndRevision()
    {
        var request = new CreateJournalEntryAdminRequest
                      {
                          Text      = "Fresh entry created by admin"
                        , Tags      = new[] { "admin", "test" }
                        , Mood      = "Happy"
                        , MoodScore = 8
                        , MoodLevel = 4
                      };

        var result = await _sut.CreateEntry(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var entryIdProp = okResult.Value.GetType().GetProperty("entryId")?.GetValue(okResult.Value)?.ToString();
        Assert.NotNull(entryIdProp);

        var savedEntry = _store.Get<JournalEntry>(entryIdProp);
        Assert.NotNull(savedEntry);
        Assert.Null(savedEntry.DeletedUtc);
    }

    // ── SoftDeleteEntry & RestoreEntry ─────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteEntry_Returns404_WhenEntryNotFound()
    {
        var result = await _sut.SoftDeleteEntry("non-existent-id");

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Journal entry 'non-existent-id' not found.", notFound.Value);
    }

    [Fact]
    public async Task SoftDeleteEntry_SetsDeletedUtcAndReason()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var entry   = new JournalEntry { Id = entryId, CreatedUtc = DateTimeOffset.UtcNow };
        await _store.Save(entry, id: entryId);

        var result = await _sut.SoftDeleteEntry(entryId, new SoftDeleteJournalAdminRequest { Reason = "Admin audit" });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var deletedEntry = _store.GetDeleted<JournalEntry>(entryId);
        Assert.NotNull(deletedEntry);
        Assert.NotNull(deletedEntry.DeletedUtc);
        Assert.Equal("Admin audit", deletedEntry.DeletedReason);
    }

    [Fact]
    public async Task RestoreEntry_ClearsDeletedUtcAndReason()
    {
        var entryId = Guid.NewGuid().ToString("N");
        var entry   = new JournalEntry
                      {
                          Id            = entryId
                        , CreatedUtc    = DateTimeOffset.UtcNow
                        , DeletedUtc    = DateTimeOffset.UtcNow
                        , DeletedReason = "Testing delete"
                      };
        await _store.Save(entry, id: entryId);
        _store.SoftDelete<JournalEntry>(entryId);

        var result = await _sut.RestoreEntry(entryId);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var restored = _store.Get<JournalEntry>(entryId);
        Assert.NotNull(restored);
        Assert.Null(restored.DeletedUtc);
        Assert.Null(restored.DeletedReason);
    }

    // ── HardDeleteEntry ────────────────────────────────────────────────────────

    [Fact]
    public async Task HardDeleteEntry_PermanentlyRemovesEntryAndRevisions()
    {
        var entryId    = Guid.NewGuid().ToString("N");
        var revisionId = Guid.NewGuid().ToString("N");

        var entry    = new JournalEntry { Id = entryId, CreatedUtc = DateTimeOffset.UtcNow };
        var revision = new JournalRevision
                       {
                           RevisionId = revisionId
                         , EntryId    = entryId
                         , CreatedUtc = DateTimeOffset.UtcNow
                         , Text       = "Revision to delete"
                       };

        await _store.Save(entry, id: entryId);
        await _store.Save(revision, id: revisionId);

        _revisionsMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                      .Returns(new List<JournalRevision> { revision });

        var result = _sut.HardDeleteEntry(entryId);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var fetchedEntry    = _store.Get<JournalEntry>(entryId) ?? _store.GetDeleted<JournalEntry>(entryId);
        var fetchedRevision = _store.Get<JournalRevision>(revisionId) ?? _store.GetDeleted<JournalRevision>(revisionId);

        Assert.Null(fetchedEntry);
        Assert.Null(fetchedRevision);
    }

    // ── UpdateRevision ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRevision_Returns400_WhenTextIsEmpty()
    {
        var request = new UpdateJournalRevisionAdminRequest { Text = "" };

        var result = await _sut.UpdateRevision("entry-1", "rev-1", request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Text is required.", badRequest.Value);
    }

    [Fact]
    public async Task UpdateRevision_Returns404_WhenRevisionNotFound()
    {
        var request = new UpdateJournalRevisionAdminRequest { Text = "Updated text" };

        var result = await _sut.UpdateRevision("entry-1", "rev-non-existent", request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Revision 'rev-non-existent' for entry 'entry-1' not found.", notFound.Value);
    }

    [Fact]
    public async Task UpdateRevision_UpdatesExistingRevisionContent()
    {
        var entryId    = Guid.NewGuid().ToString("N");
        var revisionId = Guid.NewGuid().ToString("N");

        var revision = new JournalRevision
                       {
                           RevisionId = revisionId
                         , EntryId    = entryId
                         , CreatedUtc = DateTimeOffset.UtcNow
                         , Text       = "Initial text"
                         , Tags       = new[] { "tag1" }
                       };

        await _store.Save(revision, id: revisionId);

        var updateRequest = new UpdateJournalRevisionAdminRequest
                            {
                                Text      = "Direct revision update text"
                              , Tags      = new[] { "updated" }
                              , Mood      = "Excited"
                              , MoodScore = 9
                              , MoodLevel = 5
                            };

        var result = await _sut.UpdateRevision(entryId, revisionId, updateRequest);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var updated = _store.Get<JournalRevision>(revisionId);
        Assert.NotNull(updated);
        Assert.Equal("Direct revision update text", updated.Text);
        Assert.Equal(new[] { "updated" }, updated.Tags);
        Assert.Equal("Excited", updated.Mood);
        Assert.Equal(9, updated.MoodScore);
        Assert.Equal(5, updated.MoodLevel);
    }

    // ── RepairPartitionKeys ───────────────────────────────────────────────────

    [Fact]
    public void RepairPartitionKeys_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();

        var result = controller.RepairPartitionKeys();

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    [Fact]
    public void RepairPartitionKeys_Returns200_WithRepairStatistics()
    {
        var result = _sut.RepairPartitionKeys();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }
}
