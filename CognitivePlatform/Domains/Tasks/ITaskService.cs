namespace CognitivePlatform.Api.Domains.Tasks;

public interface ITaskService
{
    TaskItem Create(TaskItem task);

    /// <summary>
    /// Creates multiple tasks in a single call. Each task is saved individually
    /// via the same Create path so all invariants (Id, CreatedAt, UpdatedAt) are
    /// enforced consistently. Returns the saved tasks in the same order they were
    /// supplied.
    /// </summary>
    IReadOnlyList<TaskItem> CreateBatch(IReadOnlyList<TaskItem> tasks);

    TaskItem? Get(Guid    id);
    TaskItem? Get(string  id);

    TaskItem? GetDeleted(Guid    id);
    TaskItem? GetDeleted(string  id);

    IReadOnlyList<TaskItem> List( DateTimeOffset? fromUtc          = null
                                , DateTimeOffset? toUtc            = null
                                , bool            includeCompleted = true );

    IReadOnlyList<TaskItem> GetActive();

    /// <summary>
    /// Returns all active tasks in their canonical display order, each paired with
    /// its 1-based display position. The ordering is:
    ///   1. DueDate ascending (nulls last)
    ///   2. Priority descending
    ///   3. CreatedAt ascending
    ///   4. SequenceNumber ascending (tiebreaker — guarantees stability for batch-created tasks)
    /// Position numbers are stable within a session as long as no mutations occur
    /// between the list and the reference.
    /// </summary>
    IReadOnlyList<(int Position, TaskItem Task)> GetOrderedActiveTasks();

    /// <summary>
    /// Resolves a 1-based display position to its TaskItem, or null if the position
    /// is out of range. Uses the same ordering as GetOrderedActiveTasks.
    /// </summary>
    TaskItem? ResolveByPosition(int position);

    DateTimeOffset? Complete(Guid   id);
    DateTimeOffset? Complete(string id);

    /// <summary>
    /// Marks multiple tasks as complete in a single call. Accepts resolved task IDs
    /// (not position references — callers must resolve positions before calling this).
    /// Returns one result per input ID describing what happened to each task.
    /// </summary>
    IReadOnlyList<BatchCompleteResult> CompleteBatch(IReadOnlyList<string> taskIds);

    /// <summary>
    /// Updates the priority and/or Eisenhower flags of an existing task.
    /// Only non-null arguments are applied; null arguments leave the current value unchanged.
    /// Returns the updated task, or null if the task was not found or is deleted.
    /// </summary>
    TaskItem? UpdatePriority( string        id
                            , TaskPriority? priority
                            , bool?         isImportant
                            , bool?         isUrgent );

    /// <summary>
    /// Persists changes made to an already-retrieved TaskItem. The caller is
    /// responsible for mutating the fields it wants to change. UpdatedAt is
    /// always refreshed by the service regardless of what the caller sets.
    /// </summary>
    TaskItem Update(TaskItem task);

    void Delete(Guid   id);
    void Delete(string id);

    void UnDelete(Guid id);

    IEnumerable<TaskItem> QueryTasks( bool?   includeCompleted
                                    , bool?   onlyUrgent
                                    , bool?   onlyImportant
                                    , string? tag );
}