using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Embeddings;
using Microsoft.Extensions.Hosting;

namespace CognitivePlatform.Api.Services;

/// <summary>
/// Runs once at startup and embeds any existing domain records that do not yet
/// have a vector entry. This covers data that existed before embedding was enabled.
/// Low-priority — uses INSERT OR REPLACE so re-running is idempotent.
/// </summary>
public sealed class EmbeddingBackfillService : BackgroundService
{
    private readonly IObjectStore                        _store;
    private readonly IEmbeddingService                   _embeddingService;
    private readonly IVectorStore                        _vectorStore;
    private readonly ILogger<EmbeddingBackfillService>   _logger;

    public EmbeddingBackfillService( IObjectStore                       store
                                   , IEmbeddingService                   embeddingService
                                   , IVectorStore                        vectorStore
                                   , ILogger<EmbeddingBackfillService>   logger )
    {
        _store            = store;
        _embeddingService = embeddingService;
        _vectorStore      = vectorStore;
        _logger           = logger;
    }

    public async Task RunBackfillAsync(CancellationToken stoppingToken)
    {
        if (!_embeddingService.IsAvailable)
        {
            _logger.LogDebug("EmbeddingBackfillService: embedding service unavailable — skipping backfill.");
            return;
        }

        var (journalEmbedded, journalFailed)  = await BackfillJournalsAsync(stoppingToken);
        var (taskEmbedded,    taskFailed)     = await BackfillTasksAsync(stoppingToken);
        var (dailyEmbedded,   dailyFailed)    = await BackfillDailyRecordsAsync(stoppingToken);

        var totalEmbedded = journalEmbedded + taskEmbedded + dailyEmbedded;
        var totalFailed   = journalFailed   + taskFailed   + dailyFailed;

        _logger.LogInformation( "EmbeddingBackfillService: pass complete — embedded {Embedded}, failed {Failed}."
                              , totalEmbedded
                              , totalFailed );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // yield so the rest of app startup completes first

        _logger.LogInformation("EmbeddingBackfillService: starting backfill pass.");
        await RunBackfillAsync(stoppingToken);
    }

    // -----------------------------------------------------------------------
    // Journal backfill
    // -----------------------------------------------------------------------

    private async Task<(int Embedded, int Failed)> BackfillJournalsAsync(CancellationToken ct)
    {
        var entries = _store.List<JournalEntry>(partitionKey: null)
                            .Where(entry => entry.DeletedUtc == null)
                            .ToList();

        if (entries.Count == 0) return (0, 0);

        var latestRevisionByEntryId = _store.List<JournalRevision>(partitionKey: null)
                                            .GroupBy(revision => revision.EntryId)
                                            .ToDictionary(group  => group.Key
                                                         , group  => group
                                                                      .OrderByDescending(revision => revision.CreatedUtc)
                                                                      .First());

        var embedded = 0;
        var failed   = 0;

        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) break;

            if (!latestRevisionByEntryId.TryGetValue(entry.Id, out var revision)) continue;

            var text = revision.Text;

            if (string.IsNullOrWhiteSpace(text)) continue;

            try
            {
                var vector = await _embeddingService.EmbedAsync(text, ct);
                if (vector.Length == 0) continue;

                await _vectorStore.SaveAsync(new VectorEntry
                                             {
                                                     Id          = $"journal:{entry.Id}"
                                                   , Domain      = "journal"
                                                   , ReferenceId = entry.Id
                                                   , Text        = text
                                                   , Embedding   = vector
                                             }, ct);
                embedded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Backfill embedding failed for journal:{EntryId}", entry.Id);
                failed++;
            }
        }

        return (embedded, failed);
    }

    // -----------------------------------------------------------------------
    // Task backfill
    // -----------------------------------------------------------------------

    private async Task<(int Embedded, int Failed)> BackfillTasksAsync(CancellationToken ct)
    {
        var tasks = _store.List<TaskItem>(partitionKey: null)
                          .Where(task => !task.IsDeleted)
                          .ToList();

        var embedded = 0;
        var failed   = 0;

        foreach (var task in tasks)
        {
            if (ct.IsCancellationRequested) break;

            var text = BuildTaskText(task);

            if (string.IsNullOrWhiteSpace(text)) continue;

            try
            {
                var vector = await _embeddingService.EmbedAsync(text, ct);
                if (vector.Length == 0) continue;

                await _vectorStore.SaveAsync(new VectorEntry
                                             {
                                                     Id          = $"task:{task.Id}"
                                                   , Domain      = "task"
                                                   , ReferenceId = task.Id
                                                   , Text        = text
                                                   , Embedding   = vector
                                             }, ct);
                embedded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Backfill embedding failed for task:{TaskId}", task.Id);
                failed++;
            }
        }

        return (embedded, failed);
    }

    // -----------------------------------------------------------------------
    // Daily record backfill
    // -----------------------------------------------------------------------

    private async Task<(int Embedded, int Failed)> BackfillDailyRecordsAsync(CancellationToken ct)
    {
        var records = _store.List<DailyRecord>(partitionKey: null)
                            .Where(record => !record.IsDeleted)
                            .ToList();

        var embedded = 0;
        var failed   = 0;

        foreach (var record in records)
        {
            if (ct.IsCancellationRequested) break;

            var text = BuildDailyRecordText(record);

            if (string.IsNullOrWhiteSpace(text)) continue;

            try
            {
                var vector = await _embeddingService.EmbedAsync(text, ct);
                if (vector.Length == 0) continue;

                await _vectorStore.SaveAsync(new VectorEntry
                                             {
                                                     Id          = $"daily:{record.Id}"
                                                   , Domain      = "daily"
                                                   , ReferenceId = record.Id
                                                   , Text        = text
                                                   , Embedding   = vector
                                             }, ct);
                embedded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Backfill embedding failed for daily:{RecordId}", record.Id);
                failed++;
            }
        }

        return (embedded, failed);
    }

    // -----------------------------------------------------------------------
    // Text builders
    // -----------------------------------------------------------------------

    private static string BuildTaskText(TaskItem task)
    {
        var parts = new[] { task.ShortDescription, task.Details }
                    .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join(" ", parts);
    }

    private static string BuildDailyRecordText(DailyRecord record)
    {
        var parts = new[] { record.OpeningText, record.ClosingText }
                    .Where(text => !string.IsNullOrWhiteSpace(text));
        var body  = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(body) ? string.Empty : $"Day {record.Date}: {body}";
    }
}
