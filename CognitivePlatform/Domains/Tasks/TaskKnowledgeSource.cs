using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Api.Domains.Tasks;

/// KNOWLEDGE SOURCE (ADAPTER)
/// -------------------------
/// Adapts a domain into the Knowledge system.
/// - Translates domain objects into KnowledgeItemDto.
/// - Implements knowledge lifecycle actions (archive, hide, etc.).
/// - May call domain services or persistence, but owns no business rules.
///
/// Rule of thumb:
/// If the Knowledge system disappeared tomorrow,
/// this class should be deleted.

/// <summary>
/// 
/// </summary>
public class TaskKnowledgeSource : IKnowledgeSource
{
    private readonly ITaskService _taskService;
    private readonly IObjectStore _objectStore;

    public KnowledgeKind Kind => KnowledgeKind.Task;

    public TaskKnowledgeSource (ITaskService taskService
                              , IObjectStore objectStore)
    {
        _taskService = taskService;
        _objectStore = objectStore;
    }

    public IEnumerable<KnowledgeItemDto> GetKnowledgeItems (KnowledgeQuery    query
                                                          , CancellationToken ct)
    {
        // TODO: introduce filtering
        // NOTE: Filtering is intentionally deferred to the aggregator for now

        var tasks = _taskService.ListTasks();

        foreach (var task in tasks)
        {
            if (query.Id is not null
             && task.Id != query.Id.Value.ToString("N"))
                continue;

            IReadOnlyList<string> tags = task.Tags is { Count: > 0 }
                                                 ? task.Tags
                                                 : Array.Empty<string>();
            yield return new KnowledgeItemDto
                         {
                                 Id        = Guid.Parse(task.Id)
                               , Kind      = Kind
                               , Title     = DeriveTitle(task)
                               , Summary   = DeriveSummary(task)
                               , CreatedAt = task.CreatedAt
                                 // , LastModifiedAt = task.TODO
                               , Status     = GetStatus(task)
                               , Tags       = tags
                               , Importance = null
                               , Urgency    = null
                         };
        }
    }

    private KnowledgeStatus GetStatus(TaskItem task)
    {
        /*
         Status
            Active → CompletedAt == null
            Archived → completed + archived (or soft-deleted)
         */
        
        // TODO: Logic I think can be refined.  I just can't think of it right now :-/
        return task.CompletedAt == null 
                       ? KnowledgeStatus.Active 
                       : task.IsDeleted // For now Delete == Archived 
                               ? KnowledgeStatus.Deleted 
                               : KnowledgeStatus.Active; 
        // More on Delete: 
        /*
         For v1, keep it simple:
            - Archive = soft delete
         Later decide:
            - complete ≠ archive
            - completed tasks still appear somewhere
         */
    }
    public void Archive (Guid              id
                       , CancellationToken ct)
    {
        // NOTE: For task, "archive" is currently implemented
        // as a soft delete. This may change in the future.
        _objectStore.SoftDelete<TaskItem>(id.ToString("N"));
    }

    private static string DeriveTitle (TaskItem task)
    {
        return task.Title
                   .Length <= 60
                       ? task.Title
                       : string.Concat(task.Title.AsSpan(0, 57), "…");
    }

    private static string? DeriveSummary (TaskItem task)
    {
        return task.Title
                   .Length <= 140
                       ? null
                       : string.Concat(task.Title.AsSpan(0, 137), "…");
    }
}