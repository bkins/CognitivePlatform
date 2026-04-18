namespace CognitivePlatform.Api.Data;

/// <summary>
/// Per-type row count returned by <see cref="SqliteObjectStore.GetObjectTypeCounts"/>.
/// Admin use only.
/// </summary>
public sealed record ObjectTypeCount
{
    public string TypeName    { get; init; } = string.Empty;
    public int    Total       { get; init; }
    public int    SoftDeleted { get; init; }
}
