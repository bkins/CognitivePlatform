using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Tasks;

public interface ITaskService
{
    TaskItem                      AddTask(TaskItem item);
    TaskItem?                     GetById(string   id);
    IReadOnlyCollection<TaskItem> GetAll();
    void                          Save(TaskItem item);
}