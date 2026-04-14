namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>Response item from GET /api/admin/knowledge.</summary>
public sealed record KnowledgeItemAdminDto
{
    public string        IdString       { get; init; } = string.Empty;
    public string        Kind           { get; init; } = string.Empty;
    public string        Title          { get; init; } = string.Empty;
    public string?       Summary        { get; init; }
    public string        Status         { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt     { get; init; }
    public DateTimeOffset LastModifiedAt { get; init; }
}

/// <summary>Request body for POST /api/admin/knowledge.</summary>
public sealed record InjectKnowledgeRequest
{
    public string  Title   { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string  Kind    { get; init; } = "Pending";
}
