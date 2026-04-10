using System.Collections;
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

        var tasks = _taskService.List();

        foreach (var task in tasks)
        {
            if (query.Id is not null
             && task.Id != query.Id.Value.ToString("N"))
                continue;

            IEnumerable tags = task.Tags is { Count: > 0 }
                                       ? task.Tags
                                       : Array.Empty<string>();
            var item = new KnowledgeItemDto
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
            
            yield return item;
        }
    }

    private static KnowledgeStatus GetStatus(TaskItem task)
    {
        if (task.IsDeleted)
            return KnowledgeStatus.Deleted;

        if (task.CompletedAt != null)
            return KnowledgeStatus.Completed;

        return KnowledgeStatus.Active;
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
        return task.ShortDescription
                   .Length <= 60
                       ? task.ShortDescription
                       : string.Concat(task.ShortDescription.AsSpan(0, 57), "…");
    }

    private static string? DeriveSummary (TaskItem task)
    {
        if (task.Details is null)
        {
            return string.Empty;
        }
        
        return task.Details?
                   .Length <= 140
                       ? null
                       : string.Concat(task.Details.AsSpan(0, 137), "…");
    }
}