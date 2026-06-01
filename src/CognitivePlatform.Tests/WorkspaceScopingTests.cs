using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

/// <summary>
/// Verifies that TaskService, JournalService, and ObjectStoreActivityLog
/// pass the active workspace partition key through to every IObjectStore call.
/// </summary>
public class WorkspaceScopingTests
{
    // ================================================================
    // TaskService
    // ================================================================

    private static (TaskService service, Mock<IObjectStore> store, Mock<IWorkspaceContext> ctx)
        BuildTaskService(string? partitionKey)
    {
        var store = new Mock<IObjectStore>();
        store.Setup(objectStore => objectStore.Save(It.IsAny<TaskItem>()
                                                  , It.IsAny<string?>()
                                                  , It.IsAny<string?>()))
             .ReturnsAsync(string.Empty);

        var ctx           = new Mock<IWorkspaceContext>();
        var embeddingMock = new Mock<IEmbeddingService>();
        var vectorMock    = new Mock<IVectorStore>();
        var loggerMock    = new Mock<ILogger<TaskService>>();

        ctx.Setup(workspaceContext => workspaceContext.ActivePartitionKey).Returns(partitionKey);
        embeddingMock.Setup(service => service.IsAvailable).Returns(false);

        return (new TaskService(store.Object, ctx.Object, embeddingMock.Object, vectorMock.Object, loggerMock.Object), store, ctx);
    }

    [Fact]
    public void TaskService_Create_UsesActivePartitionKey()
    {
        var (service, store, _) = BuildTaskService("work");

        service.Create(new TaskItem());

        store.Verify(objectStore => objectStore.Save(It.IsAny<TaskItem>()
                                                   , "work"
                                                   , It.IsAny<string?>())
                   , Times.Once);
    }

    [Fact]
    public void TaskService_Create_UsesNullPartitionKey_ForPersonalWorkspace()
    {
        var (service, store, _) = BuildTaskService(null);

        service.Create(new TaskItem());

        store.Verify(objectStore => objectStore.Save(It.IsAny<TaskItem>()
                                                   , (string?)null
                                                   , It.IsAny<string?>())
                   , Times.Once);
    }

    [Fact]
    public void TaskService_List_PassesActivePartitionKey()
    {
        var (service, store, _) = BuildTaskService("work");
        store.Setup(objectStore => objectStore.List<TaskItem>("work", null, null))
             .Returns(new List<TaskItem>());

        service.List();

        store.Verify(objectStore => objectStore.List<TaskItem>("work", null, null), Times.Once);
    }

    [Fact]
    public void TaskService_QueryTasks_PassesActivePartitionKey()
    {
        var (service, store, _) = BuildTaskService("family");
        store.Setup(objectStore => objectStore.List<TaskItem>("family", null, null))
             .Returns(new List<TaskItem>());

        service.QueryTasks(includeCompleted: null, onlyUrgent: null, onlyImportant: null, tag: null);

        store.Verify(objectStore => objectStore.List<TaskItem>("family", null, null), Times.Once);
    }

    // ================================================================
    // ObjectStoreActivityLog
    // ================================================================

    private static (ObjectStoreActivityLog log, Mock<IObjectStore> store, Mock<IWorkspaceContext> ctx)
        BuildActivityLog(string? partitionKey)
    {
        var store = new Mock<IObjectStore>();
        store.Setup(objectStore => objectStore.Save(It.IsAny<ActivityEvent>()
                                                  , It.IsAny<string?>()
                                                  , It.IsAny<string?>()))
             .ReturnsAsync(string.Empty);

        var ctx = new Mock<IWorkspaceContext>();
        ctx.Setup(workspaceContext => workspaceContext.ActivePartitionKey).Returns(partitionKey);

        return (new ObjectStoreActivityLog(store.Object, ctx.Object), store, ctx);
    }

    [Fact]
    public async Task ActivityLog_LogAsync_UsesActivePartitionKey()
    {
        var (log, store, _) = BuildActivityLog("work");
        var activityEvent   = new ActivityEvent { Id = "evt1", ActivityType = "run" };

        await log.LogAsync(activityEvent);

        store.Verify(objectStore => objectStore.Save(activityEvent, "work", "evt1"), Times.Once);
    }

    [Fact]
    public async Task ActivityLog_ListAsync_UsesActivePartitionKey()
    {
        var (log, store, _) = BuildActivityLog("work");
        store.Setup(objectStore => objectStore.List<ActivityEvent>("work", null, null))
             .Returns(new List<ActivityEvent>());

        await log.ListAsync();

        store.Verify(objectStore => objectStore.List<ActivityEvent>("work", null, null), Times.Once);
    }

    [Fact]
    public async Task ActivityLog_LogAsync_UsesNullPartitionKey_ForPersonalWorkspace()
    {
        var (log, store, _) = BuildActivityLog(null);
        var activityEvent   = new ActivityEvent { Id = "evt2", ActivityType = "meditation" };

        await log.LogAsync(activityEvent);

        store.Verify(objectStore => objectStore.Save(activityEvent, (string?)null, "evt2"), Times.Once);
    }

    // ================================================================
    // JournalService
    // ================================================================

    private static (JournalService service, Mock<IObjectStore> store, Mock<IWorkspaceContext> ctx)
        BuildJournalService(string? partitionKey)
    {
        var store           = new Mock<IObjectStore>();
        var revisionRepo    = new Mock<IJournalRevisionRepository>();
        var draftRepo       = new Mock<IJournalDraftRepository>();
        var logger          = new Mock<ILogger<JournalService>>();
        var ctx             = new Mock<IWorkspaceContext>();

        store.Setup(objectStore => objectStore.Save(It.IsAny<JournalEntry>()
                                                  , It.IsAny<string?>()
                                                  , It.IsAny<string?>()))
             .ReturnsAsync((JournalEntry entry, string? _, string? id) => id ?? entry.Id);

        store.Setup(objectStore => objectStore.Save(It.IsAny<JournalRevision>()
                                                  , It.IsAny<string?>()
                                                  , It.IsAny<string?>()))
             .ReturnsAsync(string.Empty);

        draftRepo.Setup(repo => repo.AddAsync(It.IsAny<JournalDraft>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        revisionRepo.Setup(repo => repo.GetRevisionsByEntryId(It.IsAny<string>()))
                    .Returns(new List<JournalRevision>
                             {
                                 new() { RevisionId = Guid.NewGuid().ToString("N"), Text = "test", Tags = [], MediaPaths = [] }
                             });

        var embeddingMock = new Mock<IEmbeddingService>();
        var vectorMock    = new Mock<IVectorStore>();

        ctx.Setup(workspaceContext => workspaceContext.ActivePartitionKey).Returns(partitionKey);
        embeddingMock.Setup(service => service.IsAvailable).Returns(false);

        return (new JournalService(store.Object, revisionRepo.Object, draftRepo.Object, logger.Object, ctx.Object, embeddingMock.Object, vectorMock.Object), store, ctx);
    }

    [Fact]
    public async Task JournalService_AddEntryAsync_SavesEntryWithActivePartitionKey()
    {
        var (service, store, _) = BuildJournalService("work");

        await service.AddEntryAsync("text", [], null, null, null, []);

        store.Verify(objectStore => objectStore.Save(It.IsAny<JournalEntry>()
                                                   , "work"
                                                   , It.IsAny<string?>())
                   , Times.Once);
    }

    [Fact]
    public async Task JournalService_AddEntryAsync_SavesRevisionWithActivePartitionKey()
    {
        var (service, store, _) = BuildJournalService("work");

        await service.AddEntryAsync("text", [], null, null, null, []);

        store.Verify(objectStore => objectStore.Save(It.IsAny<JournalRevision>()
                                                   , "work"
                                                   , It.IsAny<string?>())
                   , Times.Once);
    }

    [Fact]
    public void JournalService_ListEntries_PassesActivePartitionKey()
    {
        var (service, store, _) = BuildJournalService("work");
        store.Setup(objectStore => objectStore.List<JournalEntry>("work", null, null))
             .Returns(new List<JournalEntry>());

        service.ListEntries();

        store.Verify(objectStore => objectStore.List<JournalEntry>("work", null, null), Times.Once);
    }

    [Fact]
    public void JournalService_ListEntriesOnThisDay_PassesActivePartitionKey()
    {
        var (service, store, _) = BuildJournalService("work");
        store.Setup(objectStore => objectStore.List<JournalEntry>("work", null, null))
             .Returns(new List<JournalEntry>());

        service.ListEntriesOnThisDay(month: 5, day: 1);

        store.Verify(objectStore => objectStore.List<JournalEntry>("work", null, null), Times.Once);
    }

    [Fact]
    public async Task JournalService_AddEntryAsync_UsesNullPartitionKey_ForPersonalWorkspace()
    {
        var (service, store, _) = BuildJournalService(null);

        await service.AddEntryAsync("text", [], null, null, null, []);

        store.Verify(objectStore => objectStore.Save(It.IsAny<JournalEntry>()
                                                   , (string?)null
                                                   , It.IsAny<string?>())
                   , Times.Once);
    }

    [Fact]
    public void JournalService_ListAllEntries_AlwaysPassesNullPartitionKey_RegardlessOfWorkspace()
    {
        var (service, store, _) = BuildJournalService("work");
        store.Setup(objectStore => objectStore.List<JournalEntry>((string?)null, null, null))
             .Returns(new List<JournalEntry>());

        service.ListAllEntries();

        store.Verify(objectStore => objectStore.List<JournalEntry>((string?)null, null, null), Times.Once);
        store.Verify(objectStore => objectStore.List<JournalEntry>("work", null, null), Times.Never);
    }
}
