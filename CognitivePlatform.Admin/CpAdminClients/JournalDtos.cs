namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>Response item from GET /api/admin/journal/entries.</summary>
public sealed record JournalEntryAdminDto
{
    public string          Id               { get; init; } = string.Empty;
    public DateTimeOffset  CreatedUtc       { get; init; }
    public bool            IsDeleted        { get; init; }
    public string?         DeletedReason    { get; init; }
    public string?         LatestText       { get; init; }
    public DateTimeOffset? LatestRevisionAt { get; init; }
    public int             RevisionCount    { get; init; }
}

/// <summary>Response item from GET /api/admin/journal/entries/{entryId}/revisions.</summary>
public sealed record JournalRevisionAdminDto
{
    public string         RevisionId { get; init; } = string.Empty;
    public string         EntryId    { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public string         Text       { get; init; } = string.Empty;
    public string[]       Tags       { get; init; } = [];
    public string?        Mood       { get; init; }
    public int?           MoodScore  { get; init; }
    public int?           MoodLevel  { get; init; }
    public string         State      { get; init; } = string.Empty;
}

/// <summary>Request body for POST /api/admin/journal/entries/{entryId}/revisions.</summary>
public sealed record AddCorrectionRevisionRequest
{
    public string   Text      { get; init; } = string.Empty;
    public string[] Tags      { get; init; } = [];
    public string?  Mood      { get; init; }
    public int?     MoodScore { get; init; }
    public int?     MoodLevel { get; init; }
}

/// <summary>Response from POST /api/admin/journal/repair-partition-keys.</summary>
public sealed record RepairPartitionKeysResultDto
{
    public int      EntriesRepaired     { get; init; }
    public int      RevisionsRepaired   { get; init; }
    public string[] RepairedEntryIds    { get; init; } = [];
    public string[] RepairedRevisionIds { get; init; } = [];
}
