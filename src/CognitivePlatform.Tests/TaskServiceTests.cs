using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Tasks;

namespace CognitivePlatform.Tests;

public class TaskServiceTests
{
    private readonly Mock<IObjectStore> _storeMock = new();
    private readonly TaskService        _service;

    public TaskServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<TaskItem>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _service = new TaskService(_storeMock.Object);
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
        var task1 = new TaskItem { Id = Guid.NewGuid().ToString("N"), Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "work" } };
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
    public void Delete_DoesNotThrow_WhenTaskNotFound()
    {
        var id = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<TaskItem>(id.ToString("N"), null))
                  .Returns((TaskItem?)null);

        var exception = Record.Exception(() => _service.Delete(id));

        Assert.Null(exception);
    }
}
