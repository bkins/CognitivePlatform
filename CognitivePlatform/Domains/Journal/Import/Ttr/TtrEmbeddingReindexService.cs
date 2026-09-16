using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrEmbeddingReindexService
{
    private readonly IObjectStore       _store;
    private readonly IEmbeddingService  _embeddingService;
    private readonly IVectorStore       _vectorStore;

    public TtrEmbeddingReindexService(IObjectStore store, IEmbeddingService embeddingService, IVectorStore vectorStore)
    {
        _store            = store;
        _embeddingService = embeddingService;
        _vectorStore      = vectorStore;
    }

    public async Task<TtrEmbeddingReindexResult> ReindexAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var batch = _store.Get<TtrImportBatch>(batchId)
                 ?? throw new KeyNotFoundException($"TtR import batch {batchId} was not found.");
        if (!batch.VerificationPassed || batch.Status != "Completed")
            throw new InvalidOperationException("TtR embeddings can run only after journal and media verification passes.");
        if (!_embeddingService.IsAvailable)
            throw new InvalidOperationException("The configured embedding service is unavailable.");

        batch.EmbeddingStatus      = "Running";
        batch.EmbeddedCount        = 0;
        batch.EmbeddingFailedCount = 0;
        await _store.Save(batch, id: batch.Id);
        var items = _store.List<TtrImportItem>()
                          .Where(item => item.BatchId == batchId && item.Status is "Imported" or "AlreadyImported")
                          .OrderBy(item => item.SourceEntryId);
        foreach (var item in items)
        {
            try
            {
                var revision = _store.Get<JournalRevision>(item.RevisionId, WorkspaceKeys.ToPartitionKey(batch.TargetPartition))
                            ?? throw new InvalidOperationException($"Imported revision {item.RevisionId} is missing.");
                var vector = await _embeddingService.EmbedAsync(revision.Text, cancellationToken);
                if (vector.Length == 0) throw new InvalidOperationException("Embedding provider returned an empty vector.");
                await _vectorStore.SaveAsync(new VectorEntry
                                             {
                                                 Id          = $"journal:{item.EntryId}"
                                               , Domain      = "journal"
                                               , ReferenceId = item.EntryId
                                               , Text        = revision.Text
                                               , Embedding   = vector
                                             }, cancellationToken);
                batch.EmbeddedCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                batch.EmbeddingFailedCount++;
            }
            await _store.Save(batch, id: batch.Id);
        }
        batch.EmbeddingStatus = batch.EmbeddingFailedCount == 0 ? "Completed" : "Failed";
        await _store.Save(batch, id: batch.Id);
        return new TtrEmbeddingReindexResult(batch.Id, batch.EmbeddedCount, batch.EmbeddingFailedCount, batch.EmbeddingStatus);
    }
}
