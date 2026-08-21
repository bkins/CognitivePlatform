using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CognitivePlatform.Api.Domains.Tasks;

public sealed class TaskRepositoryInMemory
{
    private static readonly ConcurrentDictionary<string, TaskItem> _store = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<TaskItem> GetAll()
    {
        return _store.Values.ToList();
    }

    public TaskItem? GetById(string id)
    {
        if (id.HasNoValue())
        {
            return null;
        }


        return _store.TryGetValue(id, out var task)
                       ? task
                       : null;
    }

    public void Save(TaskItem task)
    {
        if (task.Id.HasNoValue())
        {
            task.Id = Guid.NewGuid().ToString("N");
        }

        _store[task.Id] = task;
    }
}