namespace CognitivePlatform.Api.Domains.Journal;

public sealed record JournalSubmissionRequest(DateTimeOffset AuthoredAt, string Text);
