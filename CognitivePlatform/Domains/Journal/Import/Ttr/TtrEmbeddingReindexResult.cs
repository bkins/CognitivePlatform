namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed record TtrEmbeddingReindexResult(string BatchId, int EmbeddedCount, int FailedCount, string Status);
