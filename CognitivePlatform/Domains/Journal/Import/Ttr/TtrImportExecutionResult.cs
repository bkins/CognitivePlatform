namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed record TtrImportExecutionResult(string BatchId, int ImportedCount, int AlreadyImportedCount, int FailedCount, bool VerificationPassed);
