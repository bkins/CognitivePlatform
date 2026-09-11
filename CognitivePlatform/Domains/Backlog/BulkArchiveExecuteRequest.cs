namespace CognitivePlatform.Api.Domains.Backlog;

public sealed record BulkArchiveExecuteRequest(
    int                              OlderThanDays,
    IReadOnlyList<BulkArchiveSelection> Selections,
    string                           Confirmation,
    string?                          Actor = null);
