using System;

namespace CognitivePlatform.Api.Domains.Tasks;

public class TaskItem
{
    public string          Id             { get; set; } = Guid.NewGuid().ToString("N");
    public string          Title          { get; set; } = string.Empty;
    public string?         Description    { get; set; }
    public DateTimeOffset  CreatedAt      { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DueDate        { get; set; }
    public bool            IsImportant    { get; set; }
    public bool            IsUrgent       { get; set; }
    public List<string>    Tags           { get; set; } = new();
    public bool            IsDeleted      { get; set; }
    public DateTimeOffset? CompletedAt    { get; set; }
    public string?         RecurrenceRule { get; set; }   // Future: v2 recurrence
}