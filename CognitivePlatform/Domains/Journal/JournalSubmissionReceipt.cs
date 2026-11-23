namespace CognitivePlatform.Api.Domains.Journal;

public sealed record JournalSubmissionReceipt(Guid           SubmissionId
                                            , Guid           EntryId
                                            , DateTimeOffset AuthoredAt
                                            , DateTimeOffset SubmittedAt);
