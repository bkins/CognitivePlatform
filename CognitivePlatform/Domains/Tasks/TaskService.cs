using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Workspace;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// ObjectStore is infrastructure.
/// Domain Services own meaning.
/// KnowledgeService coordinates meaning across domains.
/// </summary>
public class TaskService : ITaskService
{
    private readonly IObjectStore          _store;
    private readonly IWorkspaceContext     _workspaceContext;
    private readonly IEmbeddingService     _embeddingService;
    private readonly IVectorStore          _vectorStore;
    private readonly ILogger<TaskService>  _logger;

    // Provides a stable, monotonically increasing tiebreaker for tasks whose
    // CreatedAt timestamps are identical (e.g. tasks created in a batch loop).
    // Volatile ensures visibility across threads without a full lock.
    private static volatile int _sequenceCounter = 0;

    public TaskService( IObjectStore          store
                      , IWorkspaceContext     workspaceContext
                      , IEmbeddingService     embeddingService
                      , IVectorStore          vectorStore
                      , ILogger<TaskService>  logger )
    {
        _store            = store;
        _workspaceContext = workspaceContext;
        _embeddingService = embeddingService;
        _vectorStore      = vectorStore;
        _logger           = logger;
    }

    public TaskItem Create(TaskItem task)
    {
        var now = DateTimeOffset.UtcNow;

        if (task.Id.HasNoValue())
            task.Id = Guid.NewGuid().ToString("N");

        task.CreatedAt      = now;
        task.UpdatedAt      = now;
        task.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

        SaveInternal(task);

        QueueEmbedding("task", task.Id, BuildTaskEmbeddingText(task));

        return task;
    }

    public IReadOnlyList<TaskItem> CreateBatch(IReadOnlyList<TaskItem> tasks)
    {
        return tasks.Select(Create)
                    .ToList();
    }

    public TaskItem? Get(Guid id)
    {
        return id == Guid.Empty
                       ? throw new ArgumentException("id cannot be empty.", nameof(id))
                       : _store.Get<TaskItem>(id.ToString("N")
                                            , partitionKey: _workspaceContext.ActivePartitionKey);
    }

    public TaskItem? Get(string id)
    {
        return Get(ParseId(id));
    }

    public TaskItem? GetDeleted(Guid id)
    {
        return id == Guid.Empty
                       ? throw new ArgumentException("id cannot be empty.", nameof(id))
                       : _store.GetDeleted<TaskItem>(id.ToString("N")
                                                   , partitionKey: _workspaceContext.ActivePartitionKey);
    }

    public TaskItem? GetDeleted(string id)
    {
        return GetDeleted(ParseId(id));
    }

    public IEnumerable<TaskItem> QueryTasks( bool?   includeCompleted
                                           , bool?   onlyUrgent
                                           , bool?   onlyImportant
                                           , string? tag )
    {
        var normalizedTag = tag is null || tag.HasNoValue() ? null : tag.Trim();

        return _store.List<TaskItem>(partitionKey: _workspaceContext.ActivePartitionKey)
                     .Where(task => task.IsDeleted.Not()
                                 && (includeCompleted == true || task.CompletedAt == null)
                                 && (onlyUrgent       != true || task.IsUrgent)
                                 && (onlyImportant    != true || task.IsImportant)
                                 && (normalizedTag    == null || task.Tags.Contains(normalizedTag)));
    }

    public IReadOnlyList<TaskItem> List( DateTimeOffset? fromUtc          = null
                                       , DateTimeOffset? toUtc            = null
                                       , bool            includeCompleted = true )
    {
        var tasks = _store.List<TaskItem>(partitionKey: _workspaceContext.ActivePartitionKey
                                        , fromUtc: fromUtc
                                        , toUtc:   toUtc);

        var query = tasks.Where(taskItem => taskItem.IsDeleted.Not());

        if (includeCompleted.Not())
            query = query.Where(taskItem => taskItem.CompletedAt == null);

        return ApplyCanonicalOrder(query).ToList();
    }

    public IReadOnlyList<TaskItem> GetActive()
    {
        return List(includeCompleted: false);
    }

    public IReadOnlyList<(int Position, TaskItem Task)> GetOrderedActiveTasks()
    {
        return GetActive().Select(( task, index ) => (Position: index + 1, Task: task))
                          .ToList();
    }

    public TaskItem? ResolveByPosition(int position)
    {
        if (position < 1)
            return null;

        var ordered = GetActive();

        return position > ordered.Count
                       ? null
                       : ordered[position - 1];
    }

    public DateTimeOffset? Complete(Guid id)
    {
        var task = Get(id);

        if (task == null)
            throw new KeyNotFoundException($"Task {id} not found.");

        if (task.CompletedAt != null)
            return task.CompletedAt;

        task.CompletedAt = DateTimeOffset.UtcNow;

        SaveInternal(task);

        return task.CompletedAt;
    }

    public DateTimeOffset? Complete(string id)
    {
        return Complete(ParseId(id));
    }

    public IReadOnlyList<BatchCompleteResult> CompleteBatch(IReadOnlyList<string> taskIds)
    {
        return taskIds.Select(CompleteOne).ToList();
    }

    public TaskItem? UpdatePriority( string        id
                                   , TaskPriority? priority
                                   , bool?         isImportant
                                   , bool?         isUrgent )
    {
        var task = Get(id);

        if (task is null || task.IsDeleted)
            return null;

        if (priority    is not null) task.Priority    = priority.Value;
        if (isImportant is not null) task.IsImportant = isImportant.Value;
        if (isUrgent    is not null) task.IsUrgent    = isUrgent.Value;

        SaveInternal(task);

        return task;
    }

    public TaskItem Update(TaskItem task)
    {
        var existing = Get(task.Id);

        if (existing is null || existing.IsDeleted)
            throw new KeyNotFoundException($"Task '{task.Id}' not found.");

        SaveInternal(task);

        QueueEmbedding("task", task.Id, BuildTaskEmbeddingText(task));

        return task;
    }

    public TaskItem? UpdateDueDate(string id, DateTimeOffset? dueDate)
    {
        var task = Get(id);

        if (task is null || task.IsDeleted)
            return null;

        task.DueDate = dueDate;

        SaveInternal(task);

        return task;
    }

    public void Delete(Guid id)
    {
        var task = Get(id);

        if (task == null)
            return;

        task.IsDeleted  = true;
        task.DeletedUtc = DateTimeOffset.UtcNow;

        SaveInternal(task);

        QueueVectorDelete("task", task.Id);
    }

    public void Delete(string id)
    {
        Delete(ParseId(id));
    }

    public void UnDelete(Guid id)
    {
        var task = Get(id);

        if (task == null)
            return;

        task.IsDeleted  = false;
        task.DeletedUtc = null;

        SaveInternal(task);
    }

    public IReadOnlyList<TaskItem> GetCompleted()
    {
        return _store.List<TaskItem>()
                     .Where(task => task.IsDeleted.Not()
                                 && task.CompletedAt != null)
                     .OrderByDescending(task => task.CompletedAt)
                     .ToList();
    }

    // --- Private helpers ----------------------------------------------------

    private void QueueEmbedding(string domain, string referenceId, string text)
    {
        if (!_embeddingService.IsAvailable || string.IsNullOrWhiteSpace(text)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var embedding = await _embeddingService.EmbedAsync(text);

                if (embedding.Length == 0) return;

                await _vectorStore.SaveAsync(new VectorEntry
                                             {
                                                     Id          = $"{domain}:{referenceId}"
                                                   , Domain      = domain
                                                   , ReferenceId = referenceId
                                                   , Text        = text
                                                   , Embedding   = embedding
                                             });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Embedding failed for {Domain}:{ReferenceId}", domain, referenceId);
            }
        });
    }

    private void QueueVectorDelete(string domain, string referenceId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _vectorStore.DeleteByReferenceAsync(domain, referenceId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Vector deletion failed for {Domain}:{ReferenceId}", domain, referenceId);
            }
        });
    }

    private static string BuildTaskEmbeddingText(TaskItem task)
    {
        var parts = new[] { task.ShortDescription, task.Details }
                    .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join(" ", parts);
    }

    private BatchCompleteResult CompleteOne(string taskId)
    {
        var task = Get(taskId);

        if (task is null || task.IsDeleted)
        {
            return new BatchCompleteResult(
                TaskId:           taskId
              , ShortDescription: string.Empty
              , Outcome:          BatchCompleteOutcome.NotFound
              , CompletedAt:      null);
        }

        if (task.CompletedAt is not null)
        {
            return new BatchCompleteResult(
                TaskId:           task.Id
              , ShortDescription: task.ShortDescription
              , Outcome:          BatchCompleteOutcome.AlreadyCompleted
              , CompletedAt:      task.CompletedAt);
        }

        task.CompletedAt = DateTimeOffset.UtcNow;

        SaveInternal(task);

        return new BatchCompleteResult(
            TaskId:           task.Id
          , ShortDescription: task.ShortDescription
          , Outcome:          BatchCompleteOutcome.Completed
          , CompletedAt:      task.CompletedAt);
    }

    /// <summary>
    /// Canonical ordering used by all list and position-resolution methods.
    /// The SequenceNumber final tiebreaker guarantees stable, deterministic
    /// positions even when CreatedAt timestamps are identical (e.g. batch creation).
    /// </summary>
    private static IOrderedEnumerable<TaskItem> ApplyCanonicalOrder(IEnumerable<TaskItem> tasks)
    {
        return tasks.OrderBy(taskItem         => EisenhowerRank(taskItem))
                    .ThenByDescending(taskItem => taskItem.Priority)
                    .ThenBy(taskItem           => taskItem.DueDate ?? DateTimeOffset.MaxValue)
                    .ThenBy(taskItem           => taskItem.CreatedAt)
                    .ThenBy(taskItem           => taskItem.SequenceNumber);
    }

    /// <summary>
    /// Maps a task to its Eisenhower quadrant rank (0 = highest, 3 = lowest).
    /// Q1 — Do (important + urgent)        → 0
    /// Q2 — Decide (important + not urgent) → 1
    /// Q3 — Delegate (not important + urgent) → 2
    /// Q4 — Delete (not important + not urgent) → 3
    /// </summary>
    private static int EisenhowerRank(TaskItem task) => (task.IsImportant, task.IsUrgent) switch
    {
            (true,  true)  => 0
          , (true,  false) => 1
          , (false, true)  => 2
          , _              => 3
    };

    /// <summary>
    /// Parses a task ID string that may be either the standard dashed GUID format
    /// or the 32-character "N" format used by Guid.NewGuid().ToString("N").
    /// Using Guid.Parse alone fails on "N" format strings — this handles both.
    /// </summary>
    private static Guid ParseId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty.", nameof(id));

        if (Guid.TryParseExact(id, "N", out var guidN))
            return guidN;

        return Guid.Parse(id);
    }

    private void SaveInternal(TaskItem task)
    {
        task.UpdatedAt = DateTimeOffset.UtcNow;

        _store.Save(task
                  , partitionKey: _workspaceContext.ActivePartitionKey
                  , id:           task.Id);
    }
}
