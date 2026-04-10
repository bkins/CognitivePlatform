using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.KnowledgeInbox;

namespace CognitivePlatform.Tests;

public class TaskKnowledgeSourceTests
{
    private readonly Mock<ITaskService> _taskServiceMock = new();
    private readonly Mock<IObjectStore> _storeMock       = new();
    private readonly TaskKnowledgeSource _source;

    public TaskKnowledgeSourceTests()
    {
        _source = new TaskKnowledgeSource(_taskServiceMock.Object, _storeMock.Object);
    }

    // ================================================================
    // STATUS MAPPING
    // ================================================================

    [Fact]
    public void GetKnowledgeItems_ReturnsActiveStatus_ForNonCompletedTask()
    {
        var task = MakeTask(completed: false, deleted: false);
        SetupTaskList(task);

        var result = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(result);
        Assert.Equal(KnowledgeStatus.Active, result[0].Status);
    }

    [Fact]
    public void GetKnowledgeItems_ReturnsDeletedStatus_ForCompletedAndDeletedTask()
    {
        var task = MakeTask(completed: true, deleted: true);
        SetupTaskList(task);

        var result = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(result);
        Assert.Equal(KnowledgeStatus.Deleted, result[0].Status);
    }

    [Fact]
    public void GetKnowledgeItems_ReturnsCompletedStatus_ForCompletedNonDeletedTask()
    {
        var task = MakeTask(completed: true, deleted: false);
        SetupTaskList(task);

        var result = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(result);
        Assert.Equal(KnowledgeStatus.Completed, result[0].Status);
    }

    // ================================================================
    // TAG MAPPING
    // ================================================================

    [Fact]
    public void GetKnowledgeItems_IncludesTags_WhenTaskHasTags()
    {
        var task = MakeTask(completed: false, deleted: false, tags: new[] { "work", "focus" });
        SetupTaskList(task);

        var result = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        var tags = ((IEnumerable<string>)result[0].Tags).ToList();
        Assert.Contains("work",  tags);
        Assert.Contains("focus", tags);
    }

    [Fact]
    public void GetKnowledgeItems_UsesEmptyTags_WhenTaskHasNoTags()
    {
        var task = MakeTask(completed: false, deleted: false);
        SetupTaskList(task);

        var result = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        var tags = ((IEnumerable<string>)result[0].Tags).ToList();
        Assert.Empty(tags);
    }

    // ================================================================
    // FILTER BY ID
    // ================================================================

    [Fact]
    public void GetKnowledgeItems_ReturnsOnlyMatchingTask_WhenQueryIdIsSet()
    {
        var targetId = Guid.NewGuid();
        var target   = MakeTask(completed: false, deleted: false, id: targetId);
        var other    = MakeTask(completed: false, deleted: false);
        SetupTaskList(target, other);

        var query  = new KnowledgeQuery { Id = targetId };
        var result = _source.GetKnowledgeItems(query, CancellationToken.None).ToList();

        Assert.Single(result);
        Assert.Equal(targetId, result[0].Id);
    }

    [Fact]
    public void GetKnowledgeItems_ReturnsAllTasks_WhenQueryIdIsNull()
    {
        var task1 = MakeTask(completed: false, deleted: false);
        var task2 = MakeTask(completed: false, deleted: false);
        SetupTaskList(task1, task2);

        var result = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Equal(2, result.Count);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static TaskItem MakeTask( bool     completed
                                    , bool     deleted
                                    , Guid?    id        = null
                                    , string[]? tags     = null)
    {
        var taskId = (id ?? Guid.NewGuid()).ToString("N");
        return new TaskItem
               {
                       Id               = taskId
                     , ShortDescription = "A task"
                     , CompletedAt      = completed ? DateTimeOffset.UtcNow : null
                     , IsDeleted        = deleted
                     , Tags             = tags is { Length: > 0 }
                                                  ? new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase)
                                                  : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
               };
    }

    private void SetupTaskList(params TaskItem[] tasks)
    {
        _taskServiceMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<DateTimeOffset?>()
                                                     , It.IsAny<bool>()))
                        .Returns(tasks.ToList());
    }
}
