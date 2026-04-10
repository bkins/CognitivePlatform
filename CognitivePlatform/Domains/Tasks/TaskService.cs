using CognitivePlatform.Api.Data;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// ObjectStore is infrastructure.
/// Domain Services own meaning.
/// KnowledgeService coordinates meaning across domains.
/// </summary>
public class TaskService : ITaskService
{
    private readonly IObjectStore _store;

    // Provides a stable, monotonically increasing tiebreaker for tasks whose
    // CreatedAt timestamps are identical (e.g. tasks created in a batch loop).
    // Volatile ensures visibility across threads without a full lock.
    private static volatile int _sequenceCounter = 0;

    public TaskService(IObjectStore store)
    {
        _store = store;
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
                       : _store.Get<TaskItem>(id.ToString("N"), partitionKey: null);
    }

    public TaskItem? Get(string id)
    {
        return Get(ParseId(id));
    }

    public TaskItem? GetDeleted(Guid id)
    {
        return id == Guid.Empty
                       ? throw new ArgumentException("id cannot be empty.", nameof(id))
                       : _store.GetDeleted<TaskItem>(id.ToString("N"), partitionKey: null);
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

        return _store.List<TaskItem>()
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
        var tasks = _store.List<TaskItem>(partitionKey: null
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

        return task;
    }

    public void Delete(Guid id)
    {
        var task = Get(id);

        if (task == null)
            return;

        task.IsDeleted = true;

        SaveInternal(task);
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

        task.IsDeleted = false;

        SaveInternal(task);
    }

    // --- Private helpers ----------------------------------------------------

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
        return tasks.OrderBy(taskItem         => taskItem.DueDate ?? DateTimeOffset.MaxValue)
                    .ThenByDescending(taskItem => taskItem.Priority)
                    .ThenBy(taskItem           => taskItem.CreatedAt)
                    .ThenBy(taskItem           => taskItem.SequenceNumber);
    }

    /// <summary>
    /// Parses a task ID string that may be either the standard dashed GUID format
    /// or the 32-character "N" format used by Guid.NewGuid().ToString("N").
    /// Using Guid.Parse alone fails on "N" format strings — this handles both.
    /// </summary>
    private static Guid ParseId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty.", nameof(id));

        // "N" format: 32 hex chars with no dashes (e.g. "ddc5dd4442ef47e6993896eef66dfa71")
        // "D" format: standard dashes (e.g. "ddc5dd44-42ef-47e6-9938-96eef66dfa71")
        // Guid.ParseExact("N") handles the former; Guid.Parse handles the latter.
        // TryParseExact with "N" first covers the common case; fall back to Parse for others.
        if (Guid.TryParseExact(id, "N", out var guidN))
            return guidN;

        return Guid.Parse(id);
    }

    private void SaveInternal(TaskItem task)
    {
        task.UpdatedAt = DateTimeOffset.UtcNow;

        _store.Save(task, partitionKey: null, id: task.Id);
    }
}