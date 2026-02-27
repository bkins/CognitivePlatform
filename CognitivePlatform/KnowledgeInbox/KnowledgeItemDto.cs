namespace CognitivePlatform.Api.KnowledgeInbox;

public sealed class KnowledgeItemDto
{
    public Guid Id { get; init; }

    // What kind of thing is this?
    public KnowledgeKind Kind { get; init; }

    // Human handles
    public string  Title   { get; init; } = string.Empty;
    public string? Summary { get; init; }

    // Temporal anchors
    public DateTimeOffset CreatedAt      { get; init; }
    public DateTimeOffset LastModifiedAt { get; init; }

    // Lifecycle
    public KnowledgeStatus Status { get; set; }

    // Organization
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public bool IsEdited { get; set; }
    
    // Optional signals (nullable on purpose)
    public int? Importance { get; init; }
    public int? Urgency    { get; init; }
}

public enum KnowledgeKind
{
    Journal
  , Task
}

public enum KnowledgeStatus
{
    Active
  , Completed
  , Archived
  , Deleted
}
