namespace CognitivePlatform.Api.Domains.Journal;

internal sealed record StoredJournalSubmission(JournalSubmissionReceipt Receipt, string TextHash);
