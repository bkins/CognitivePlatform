namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>Response item from GET /api/admin/logs.</summary>
public sealed record LogEntryDto
{
    public DateTime TimestampUtc { get; init; }
    public string   Level        { get; init; } = string.Empty;
    public string   Category     { get; init; } = string.Empty;
    public string   Message      { get; init; } = string.Empty;
}
