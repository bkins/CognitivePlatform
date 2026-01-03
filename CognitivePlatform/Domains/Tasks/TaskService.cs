using System;
using System.Collections.Generic;
using System.Linq;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Tasks;

public class TaskService : ITaskService
{
    private readonly IObjectStore _store;

    public TaskService(IObjectStore store)
    {
        _store = store;
    }

    public TaskItem AddTask(TaskItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            item.Id = Guid.NewGuid().ToString("N");
        }

        item.CreatedAt = DateTimeOffset.UtcNow;

        _store.Save(item, partitionKey: null, id: item.Id);

        return item;
    }

    public TaskItem? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return _store.Get<TaskItem>(id, partitionKey: null);
    }

    public IReadOnlyCollection<TaskItem> GetAll()
    {
        return _store
               .List<TaskItem>(partitionKey: null)
               .Where(task => task.IsDeleted.Not())
               .OrderBy(task => task.CreatedAt)
               .ToList();
    }

    public void Save(TaskItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
            throw new InvalidOperationException("Task must have an Id before saving.");

        _store.Save(item, partitionKey: null, id: item.Id);
    }
}