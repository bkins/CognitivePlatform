using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class TaskServiceTests
{
    private readonly Mock<IObjectStore>          _storeMock            = new();
    private readonly Mock<IWorkspaceContext>      _workspaceContextMock = new();
    private readonly Mock<IEmbeddingService>      _embeddingMock        = new();
    private readonly Mock<IVectorStore>           _vectorStoreMock      = new();
    private readonly Mock<ILogger<TaskService>>   _loggerMock           = new();
    private readonly TaskService                  _service;

    public TaskServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<TaskItem>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _workspaceContextMock.Setup(ctx => ctx.ActivePartitionKey)
                             .Returns((string?)null); // personal workspace

        _embeddingMock.Setup(service => service.IsAvailable).Returns(false); // off by default

        _service = new TaskService(_storeMock.Object
                                 , _workspaceContextMock.Object
                                 , _embeddingMock.Object
                                 , _vectorStoreMock.Object
                                 , _loggerMock.Object);
    }

    // ================================================================
    // CREATE
    // ================================================================

    [Fact]
    public void Create_AssignsId_WhenIdIsEmpty()
    {
        var task = new TaskItem { Id = string.Empty };

        var result = _service.Create(task);

        Assert.NotEmpty(result.Id);
    }

    [Fact]
    public void Create_PreservesExistingId_WhenIdIsAlreadySet()
    {
        var existingId = Guid.NewGuid().ToString("N");
        var task       = new TaskItem { Id = existingId };

        var result = _service.Create(task);

        Assert.Equal(existingId, result.Id);
    }

    [Fact]
    public void Create_SetsCreatedAt_OnCreate()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var task   = new TaskItem { Id = string.Empty };

        var result = _service.Create(task);

        Assert.True(result.CreatedAt >= before);
    }

    [Fact]
    public void Create_SetsUpdatedAt_OnCreate()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var task   = new TaskItem { Id = string.Empty };

        var result = _service.Create(task);

        Assert.True(result.UpdatedAt >= before);
    }

    [Fact]
    public void Create_AssignsPositiveSequenceNumber()
    {
        var task = new TaskItem { Id = string.Empty };

        var result = _service.Create(task);

        Assert.True(result.SequenceNumber > 0);
    }

    // ================================================================
    // CREATE BATCH
    // ================================================================

    [Fact]
    public void CreateBatch_CreatesAllItems()
    {
        var tasks = new List<TaskItem>
        {
              new() { Id = string.Empty }
            , new() { Id = string.Empty }
            , new() { Id = string.Empty }
        };

        var results = _service.CreateBatch(tasks);

        Assert.Equal(3, results.Count);
        Assert.All(results, taskItem => Assert.NotEmpty(taskItem.Id));
    }

    [Fact]
    public void CreateBatch_AssignsUniqueSequenceNumbers()
    {
        var tasks = new List<TaskItem>
        {
              new() { Id = string.Empty }
            , new() { Id = string.Empty }
            , new() { Id = string.Empty }
        };

        var results = _service.CreateBatch(tasks);

        var sequenceNumbers = results.Select(taskItem => taskItem.SequenceNumber).ToList();
        Assert.Equal(sequenceNumbers.Count, sequenceNumbers.Distinct().Count());
    }

    // ================================================================
    // GET
    // ================================================================

    [Fact]
    public void Get_Throws_WhenIdIsGuidEmpty()
    {
        Assert.Throws<ArgumentException>(() => _service.Get(Guid.Empty));
    }

    [Fact]
    public void Get_DelegatesToStore_WithFormattedId()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem { Id = id.ToString("N") };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        var result = _service.Get(id);

        Assert.Equal(task, result);
    }

    // ================================================================
    // GET DELETED
    // ================================================================

    [Fact]
    public void GetDeleted_Throws_WhenIdIsGuidEmpty()
    {
        Assert.Throws<ArgumentException>(() => _service.GetDeleted(Guid.Empty));
    }

    [Fact]
    public void GetDeleted_DelegatesToStore_WithFormattedId()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem { Id = id.ToString("N"), IsDeleted = true };

        _storeMock.Setup(store => store.GetDeleted<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        var result = _service.GetDeleted(id);

        Assert.Equal(task, result);
    }

    // ================================================================
    // QUERY TASKS
    // ================================================================

    [Fact]
    public void QueryTasks_ExcludesCompletedTasks_WhenIncludeCompletedIsFalse()
    {
        var active    = new TaskItem { Id = Guid.NewGuid().ToString("N") };
        var completed = new TaskItem { Id = Guid.NewGuid().ToString("N"), CompletedAt = DateTimeOffset.UtcNow };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { active, completed });

        var results = _service.QueryTasks(includeCompleted: false
                                        , onlyUrgent:       null
                                        , onlyImportant:    null
                                        , tag:              null).ToList();

        Assert.Single(results);
        Assert.Equal(active.Id, results[0].Id);
    }

    [Fact]
    public void QueryTasks_IncludesCompletedTasks_WhenIncludeCompletedIsTrue()
    {
        var active    = new TaskItem { Id = Guid.NewGuid().ToString("N") };
        var completed = new TaskItem { Id = Guid.NewGuid().ToString("N"), CompletedAt = DateTimeOffset.UtcNow };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { active, completed });

        var results = _service.QueryTasks(includeCompleted: true
                                        , onlyUrgent:       null
                                        , onlyImportant:    null
                                        , tag:              null).ToList();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void QueryTasks_ReturnsOnlyUrgentTasks_WhenOnlyUrgentIsTrue()
    {
        var urgent    = new TaskItem { Id = Guid.NewGuid().ToString("N"), IsUrgent = true };
        var nonUrgent = new TaskItem { Id = Guid.NewGuid().ToString("N"), IsUrgent = false };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { urgent, nonUrgent });

        var results = _service.QueryTasks(includeCompleted: null
                                        , onlyUrgent:       true
                                        , onlyImportant:    null
                                        , tag:              null).ToList();

        Assert.Single(results);
        Assert.Equal(urgent.Id, results[0].Id);
    }

    [Fact]
    public void QueryTasks_ReturnsOnlyImportantTasks_WhenOnlyImportantIsTrue()
    {
        var important    = new TaskItem { Id = Guid.NewGuid().ToString("N"), IsImportant = true };
        var unimportant  = new TaskItem { Id = Guid.NewGuid().ToString("N"), IsImportant = false };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { important, unimportant });

        var results = _service.QueryTasks(includeCompleted: null
                                        , onlyUrgent:       null
                                        , onlyImportant:    true
                                        , tag:              null).ToList();

        Assert.Single(results);
        Assert.Equal(important.Id, results[0].Id);
    }

    [Fact]
    public void QueryTasks_FiltersToMatchingTag_WhenTagIsProvided()
    {
        var workTask  = new TaskItem { Id = Guid.NewGuid().ToString("N"), Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "work" } };
        var homeTask  = new TaskItem { Id = Guid.NewGuid().ToString("N"), Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "home" } };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { workTask, homeTask });

        var results = _service.QueryTasks(includeCompleted: null
                                        , onlyUrgent:       null
                                        , onlyImportant:    null
                                        , tag:              "work").ToList();

        Assert.Single(results);
        Assert.Equal(workTask.Id, results[0].Id);
    }

    [Fact]
    public void QueryTasks_DoesNotFilterByTag_WhenTagIsNull()
    {
        var task1 = new TaskItem
                    {
                            Id   = Guid.NewGuid().ToString("N")
                          , Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "work" }
                    };
        var task2 = new TaskItem { Id = Guid.NewGuid().ToString("N") };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { task1, task2 });

        var results = _service.QueryTasks(includeCompleted: null
                                        , onlyUrgent:       null
                                        , onlyImportant:    null
                                        , tag:              null).ToList();

        Assert.Equal(2, results.Count);
    }

    // ENH-04: ListTasks must never surface deleted tasks regardless of filters.
    [Fact]
    public void QueryTasks_ExcludesDeletedTasks_Unconditionally()
    {
        var active  = new TaskItem { Id = Guid.NewGuid().ToString("N"), IsDeleted = false };
        var deleted = new TaskItem { Id = Guid.NewGuid().ToString("N"), IsDeleted = true };

        _storeMock.Setup(store => store.List<TaskItem>(It.IsAny<string?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<TaskItem> { active, deleted });

        var results = _service.QueryTasks(includeCompleted: true   // even with all filters relaxed
                                        , onlyUrgent:       null
                                        , onlyImportant:    null
                                        , tag:              null).ToList();

        Assert.Single(results);
        Assert.Equal(active.Id, results[0].Id);
    }

    // ================================================================
    // COMPLETE
    // ================================================================

    [Fact]
    public void Complete_SetsCompletedAt_WhenTaskExists()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem { Id = id.ToString("N") };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        _service.Complete(id);

        Assert.NotNull(task.CompletedAt);
        Assert.True(task.CompletedAt >= before);
    }

    [Fact]
    public void Complete_IsIdempotent_WhenTaskAlreadyCompleted()
    {
        var id          = Guid.NewGuid();
        var completedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var task        = new TaskItem { Id = id.ToString("N"), CompletedAt = completedAt };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        _service.Complete(id);

        Assert.Equal(completedAt, task.CompletedAt);
    }

    [Fact]
    public void Complete_Throws_WhenTaskNotFound()
    {
        var id = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns((TaskItem?)null);

        Assert.Throws<KeyNotFoundException>(() => _service.Complete(id));
    }

    // ================================================================
    // DELETE
    // ================================================================

    [Fact]
    public void Delete_SetsIsDeletedTrue_WhenTaskExists()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem { Id = id.ToString("N") };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        _service.Delete(id);

        Assert.True(task.IsDeleted);
    }

    [Fact]
    public void Delete_SetsDeletedUtc_WhenTaskExists()
    {
        var id     = Guid.NewGuid();
        var task   = new TaskItem { Id = id.ToString("N") };
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        _service.Delete(id);

        Assert.NotNull(task.DeletedUtc);
        Assert.True(task.DeletedUtc >= before);
    }

    [Fact]
    public void Delete_DoesNotThrow_WhenTaskNotFound()
    {
        var id = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns((TaskItem?)null);

        var exception = Record.Exception(() => _service.Delete(id));

        Assert.Null(exception);
    }

    // ================================================================
    // UN-DELETE
    // ================================================================

    [Fact]
    public void UnDelete_DoesNotThrow_WhenTaskNotFound()
    {
        var id = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns((TaskItem?)null);

        var exception = Record.Exception(() => _service.UnDelete(id));

        Assert.Null(exception);
    }

    [Fact]
    public void UnDelete_ClearsIsDeletedAndDeletedUtc_WhenTaskExists()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem
                   {
                           Id         = id.ToString("N")
                         , IsDeleted  = true
                         , DeletedUtc = DateTimeOffset.UtcNow.AddDays(-1)
                   };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        _service.UnDelete(id);

        Assert.False(task.IsDeleted);
        Assert.Null(task.DeletedUtc);
    }

    // ================================================================
    // UPDATE DUE DATE
    // ================================================================

    [Fact]
    public void UpdateDueDate_SetsDueDate_WhenTaskExists()
    {
        var id      = Guid.NewGuid();
        var task    = new TaskItem { Id = id.ToString("N") };
        var dueDate = DateTimeOffset.UtcNow.AddDays(3);

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        var result = _service.UpdateDueDate(id.ToString("N"), dueDate);

        Assert.NotNull(result);
        Assert.Equal(dueDate, result!.DueDate);
    }

    [Fact]
    public void UpdateDueDate_ClearsDueDate_WhenNullIsPassed()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem { Id = id.ToString("N"), DueDate = DateTimeOffset.UtcNow.AddDays(1) };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        var result = _service.UpdateDueDate(id.ToString("N"), null);

        Assert.NotNull(result);
        Assert.Null(result!.DueDate);
    }

    [Fact]
    public void UpdateDueDate_ReturnsNull_WhenTaskNotFound()
    {
        var id = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns((TaskItem?)null);

        var result = _service.UpdateDueDate(id.ToString("N"), DateTimeOffset.UtcNow);

        Assert.Null(result);
    }

    [Fact]
    public void UpdateDueDate_ReturnsNull_WhenTaskIsDeleted()
    {
        var id   = Guid.NewGuid();
        var task = new TaskItem { Id = id.ToString("N"), IsDeleted = true };

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns(task);

        var result = _service.UpdateDueDate(id.ToString("N"), DateTimeOffset.UtcNow);

        Assert.Null(result);
    }
    // ================================================================
    // CANONICAL SORT ORDER (BUG-24)
    // ================================================================

    [Fact]
    public void GetActive_SortsQ1BeforeQ2_WhenBothHaveNoDueDate()
    {
        var q2Task = new TaskItem { Id = "q2", IsImportant = true,  IsUrgent = false, ShortDescription = "Decide" };
        var q1Task = new TaskItem { Id = "q1", IsImportant = true,  IsUrgent = true,  ShortDescription = "Do" };

        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem> { q2Task, q1Task });

        var result = _service.GetActive();

        Assert.Equal("q1", result[0].Id);
        Assert.Equal("q2", result[1].Id);
    }

    [Fact]
    public void GetActive_SortsAllFourQuadrantsCorrectly()
    {
        var q4 = new TaskItem { Id = "q4", IsImportant = false, IsUrgent = false };
        var q3 = new TaskItem { Id = "q3", IsImportant = false, IsUrgent = true  };
        var q2 = new TaskItem { Id = "q2", IsImportant = true,  IsUrgent = false };
        var q1 = new TaskItem { Id = "q1", IsImportant = true,  IsUrgent = true  };

        // Deliberately supply in worst-case reverse order
        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem> { q4, q3, q2, q1 });

        var result = _service.GetActive();

        Assert.Equal("q1", result[0].Id);
        Assert.Equal("q2", result[1].Id);
        Assert.Equal("q3", result[2].Id);
        Assert.Equal("q4", result[3].Id);
    }

    [Fact]
    public void GetActive_SortsQ2BeforeQ4_WhenQ4HasEarlierDueDate()
    {
        // This is the core BUG-24 scenario: a Q2 (Decide) task with no due date
        // must appear BEFORE a Q4 (Delete) task that has an early due date.
        var q4WithDue = new TaskItem { Id = "q4", IsImportant = false, IsUrgent = false, DueDate = DateTimeOffset.UtcNow.AddDays(1) };
        var q2NoDue   = new TaskItem { Id = "q2", IsImportant = true,  IsUrgent = false };

        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem> { q4WithDue, q2NoDue });

        var result = _service.GetActive();

        Assert.Equal("q2", result[0].Id);
        Assert.Equal("q4", result[1].Id);
    }

    [Fact]
    public void GetActive_SortsHigherPriorityFirst_WithinSameEisenhowerQuadrant()
    {
        var normalPriority   = new TaskItem { Id = "normal",   IsImportant = false, IsUrgent = false, Priority = TaskPriority.Normal };
        var criticalPriority = new TaskItem { Id = "critical", IsImportant = false, IsUrgent = false, Priority = TaskPriority.Critical };
        var lowPriority      = new TaskItem { Id = "low",      IsImportant = false, IsUrgent = false, Priority = TaskPriority.Low };

        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem> { lowPriority, normalPriority, criticalPriority });

        var result = _service.GetActive();

        Assert.Equal("critical", result[0].Id);
        Assert.Equal("normal",   result[1].Id);
        Assert.Equal("low",      result[2].Id);
    }

    [Fact]
    public void GetActive_SortsEarlierDueDateFirst_WithinSameQuadrantAndPriority()
    {
        var soonDue = new TaskItem { Id = "soon", IsImportant = true, IsUrgent = true, DueDate = DateTimeOffset.UtcNow.AddDays(1) };
        var laterDue = new TaskItem { Id = "later", IsImportant = true, IsUrgent = true, DueDate = DateTimeOffset.UtcNow.AddDays(7) };
        var noDue    = new TaskItem { Id = "none",  IsImportant = true, IsUrgent = true };

        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem> { noDue, laterDue, soonDue });

        var result = _service.GetActive();

        Assert.Equal("soon",  result[0].Id);
        Assert.Equal("later", result[1].Id);
        Assert.Equal("none",  result[2].Id);
    }

    // ================================================================
    // EMBEDDING (fire-and-forget)
    // ================================================================

    [Fact]
    public async Task Create_QueuesEmbedding_WhenEmbeddingServiceIsAvailable()
    {
        var vectorStoreTcs = new TaskCompletionSource<bool>();
        _embeddingMock.Setup(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new float[] { 0.1f, 0.2f });
        _vectorStoreMock.Setup(store => store.SaveAsync(It.IsAny<VectorEntry>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask)
                        .Callback(() => vectorStoreTcs.TrySetResult(true));

        var task = new TaskItem { ShortDescription = "Write tests", Details = "Unit tests for the parser" };

        _service.Create(task);

        await Task.WhenAny(vectorStoreTcs.Task, Task.Delay(1000));

        _embeddingMock.Verify(service => service.EmbedAsync("Write tests Unit tests for the parser"
                                                           , It.IsAny<CancellationToken>())
                            , Times.Once);
        _vectorStoreMock.Verify(store => store.SaveAsync(It.Is<VectorEntry>(entry => entry.Domain == "task"
                                                                                  && entry.ReferenceId == task.Id)
                                                       , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

    [Fact]
    public async Task Create_SkipsEmbedding_WhenEmbeddingServiceIsUnavailable()
    {
        _embeddingMock.Setup(service => service.IsAvailable).Returns(false);

        _service.Create(new TaskItem { ShortDescription = "Write tests" });

        await Task.Delay(50);

        _embeddingMock.Verify(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_QueuesVectorDelete_WhenTaskExists()
    {
        var taskId    = Guid.NewGuid();
        var task      = new TaskItem { Id = taskId.ToString("N"), ShortDescription = "Old task" };
        var deleteTcs = new TaskCompletionSource<bool>();

        _storeMock.Setup(store => store.Get<TaskItem>(taskId.ToString("N")
                                                     , It.IsAny<string?>()))
                  .Returns(task);
        _vectorStoreMock.Setup(store => store.DeleteByReferenceAsync(It.IsAny<string>()
                                                                    , It.IsAny<string>()
                                                                    , It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask)
                        .Callback(() => deleteTcs.TrySetResult(true));

        _service.Delete(taskId);

        await Task.WhenAny(deleteTcs.Task, Task.Delay(1000));

        _vectorStoreMock.Verify(store => store.DeleteByReferenceAsync("task"
                                                                     , taskId.ToString("N")
                                                                     , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

}
