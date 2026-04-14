using System;

namespace CognitivePlatform.Api.Domains.Tasks;

public class TaskItem
{
    public string          Id               { get; set; } = Guid.NewGuid().ToString("N");
    public string          ShortDescription { get; set; } = string.Empty;
    public string?         Details          { get; set; }
    public TaskPriority    Priority         { get; set; } = TaskPriority.Normal;
    public DateTimeOffset  CreatedAt        { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset  UpdatedAt        { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DueDate          { get; set; }
    public DateTimeOffset? CompletedAt      { get; set; }
    public bool             IsDeleted        { get; set; } = false;
    public DateTimeOffset?  DeletedUtc       { get; set; }

    /// <summary>
    /// The date this task was first created as part of a daily plan (via Plan: or Check:).
    /// Null for tasks created outside the daily planning flow.
    /// </summary>
    public DateOnly?        OriginDate       { get; set; }
    public HashSet<string> Tags             { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool            IsImportant      { get; set; }
    public bool            IsUrgent         { get; set; }

    /// <summary>
    /// Monotonically increasing counter assigned by TaskService.Create.
    /// Guarantees stable, deterministic ordering when CreatedAt timestamps
    /// are identical (e.g. tasks created in a tight batch loop).
    /// </summary>
    public long SequenceNumber { get; set; }

    public Dictionary<string, string> Meta { get; set; } = new();

    public static class MetaKeys
    {
        public const string Project      = "project";
        public const string ParentTaskId = "parentTaskId";
        public const string Recurrence   = "recurrence";
        public const string Goal         = "goal";
        public const string ExternalRef  = "externalRef";
    }
}