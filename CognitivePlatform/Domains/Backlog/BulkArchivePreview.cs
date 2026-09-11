namespace CognitivePlatform.Api.Domains.Backlog;

public sealed record BulkArchivePreview(
    int                              OlderThanDays,
    DateTimeOffset                   CutoffUtc,
    string                           ConfirmationText,
    IReadOnlyList<BulkArchiveCandidate> Candidates);
