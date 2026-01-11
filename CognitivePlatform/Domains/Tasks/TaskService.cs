using System;
using System.Collections.Generic;
using System.Linq;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// ObjectStore is infrastructure.
/// Domain Services own meaning.
/// KnowledgeService coordinates meaning across domains.
/// </summary>
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

    /*
     public IReadOnlyList<JournalEntry> ListEntries (DateTimeOffset? fromUtc = null
                                                  , DateTimeOffset? toUtc   = null)
    {
        var entries = _store.List<JournalEntry>(partitionKey: null
                                              , fromUtc: fromUtc
                                              , toUtc: toUtc);

        return entries.OrderBy(entry => entry.CreatedUtc)
                      .ToList();
    }
     */
    public IReadOnlyList<TaskItem> ListTasks (DateTimeOffset? fromUtc = null
                                            , DateTimeOffset? toUtc   = null)
    {
        var entries = _store.List<TaskItem>(partitionKey: null
                                          , fromUtc: fromUtc
                                          , toUtc: toUtc);

        return entries.OrderBy(entry => entry.CompletedAt)
                      .ToList();
    }
 
    public TaskItem? GetById(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("id cannot be empty.", nameof(id));

        // Your entries are stored using Guid.ToString("N")
        var stringId = id.ToString("N");

        return GetTask(stringId);
        
    }

    public void Complete (Guid id) // (sets CompletedAt)
    {
        var task = GetById(id);

        if (task == null) throw new KeyNotFoundException($"Task with id {id} not found.");
        
        task.CompletedAt = DateTimeOffset.UtcNow;
        
        _store.Save(task, partitionKey: null);
    }
    
    public TaskItem? GetTask (string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id cannot be null or empty."
                                      , nameof(id));

        return _store.Get<TaskItem>(id
                                  , partitionKey: null);
    }

    public IReadOnlyCollection<TaskItem> GetAll()
    {
        return _store.List<TaskItem>(partitionKey: null)
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
    
    // Future methods??  Or remove??
    public bool DeleteTask (string id) => throw new NotImplementedException();
    

}