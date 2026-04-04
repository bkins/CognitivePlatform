namespace CognitivePlatform.Api.Domains.Tasks;

public sealed class TaskDto
{
    public string                Id               { get; init; } = string.Empty;
    public string                ShortDescription { get; init; } = string.Empty;
    public string?               Details          { get; init; }
    public TaskPriority          Priority         { get; init; }
    public bool                  IsImportant      { get; init; }
    public bool                  IsUrgent         { get; init; }
    public DateTimeOffset        CreatedAt        { get; init; }
    public DateTimeOffset        UpdatedAt        { get; init; }
    public DateTimeOffset?       DueDate          { get; init; }
    public DateTimeOffset?       CompletedAt      { get; init; }
    public IReadOnlyList<string> Tags             { get; init; } = [];

    public static TaskDto From( TaskItem task )
        => new()
           {
                   Id               = task.Id
                 , ShortDescription = task.ShortDescription
                 , Details          = task.Details
                 , Priority         = task.Priority
                 , IsImportant      = task.IsImportant
                 , IsUrgent         = task.IsUrgent
                 , CreatedAt        = task.CreatedAt
                 , UpdatedAt        = task.UpdatedAt
                 , DueDate          = task.DueDate
                 , CompletedAt      = task.CompletedAt
                 , Tags             = task.Tags.ToList()
           };
}