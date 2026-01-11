using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Tasks;

public interface ITaskService
{
    /* Must haves:
     *  - AddTask(string title)
     *  - ListTasks()
     *  - GetById(Guid id)
     *  - Complete(Guid id) (sets CompletedAt)
     */
    TaskItem                      AddTask(TaskItem           item);
    IReadOnlyList<TaskItem>       ListTasks (DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc   = null);
    TaskItem?                     GetById(Guid               id);
    void                          Complete (Guid             id); // (sets CompletedAt)
    
    
    IReadOnlyCollection<TaskItem> GetAll();
    void                          Save (TaskItem item);

    
    // Future methods??  Or remove??
    public TaskItem? GetTask (string id);

    public bool DeleteTask (string id);
    
}