namespace CognitivePlatform.Api.Integrations.FileSync.Models;

public sealed record SyncResult
{
    public int                         FilesCopied  { get; init; }
    public int                         FilesSkipped { get; init; }
    public IReadOnlyList<FileConflict> Conflicts    { get; init; } = [];
    public string                      Summary      { get; init; } = string.Empty;
}
