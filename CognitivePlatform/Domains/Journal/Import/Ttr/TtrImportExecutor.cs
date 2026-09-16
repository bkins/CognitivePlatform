using System.Security.Cryptography;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Media;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportExecutor
{
    private readonly IObjectStore                _store;
    private readonly IHistoricalJournalWriter    _journalWriter;
    private readonly IMediaAttachmentService     _mediaService;
    private readonly ITtrMediaContentReader      _mediaReader;
    private readonly string                      _targetEnvironment;
    private readonly string                      _targetDatabasePath;

    public TtrImportExecutor(IObjectStore store
                           , IHistoricalJournalWriter journalWriter
                           , IMediaAttachmentService mediaService
                           , ITtrMediaContentReader mediaReader
                           , string targetEnvironment
                           , string targetDatabasePath)
    {
        _store              = store;
        _journalWriter      = journalWriter;
        _mediaService       = mediaService;
        _mediaReader        = mediaReader;
        _targetEnvironment  = targetEnvironment;
        _targetDatabasePath = Path.GetFullPath(targetDatabasePath);
    }

    public async Task<TtrImportExecutionResult> ExecuteAsync(TtrImportPlanRequest source
                                                            , TtrImportPlan plan
                                                            , TtrImportExecutionRequest request
                                                            , CancellationToken cancellationToken = default)
    {
        await ValidateExecutionAsync(source, plan, request, cancellationToken);
        var batchId = TtrDeterministicIdentity.CreateBatchId(plan.LogicalSourceInstance, plan.PlanSha256).ToString();
        var batch   = _store.Get<TtrImportBatch>(batchId) ?? new TtrImportBatch
                      {
                          Id                   = batchId
                        , SourceSha256         = plan.SourceSha256
                        , PlanSha256           = plan.PlanSha256
                        , BackupManifestSha256 = request.BackupManifestSha256
                        , TargetPartition      = plan.TargetPartition
                        , StartedUtc            = DateTimeOffset.UtcNow
                      };
        batch.Status               = "Running";
        batch.ImportedCount        = 0;
        batch.AlreadyImportedCount = 0;
        batch.FailedCount          = 0;
        batch.VerificationPassed   = false;
        await _store.Save(batch, id: batch.Id);

        foreach (var entry in plan.Entries.Where(item => item.Disposition == "Ready").OrderBy(item => item.SourceEntryId))
        {
            var attachmentIds = plan.Media.Where(media => media.SourceEntryId == entry.SourceEntryId)
                                          .OrderBy(media => media.SourceMediaId)
                                          .ThenBy(media => media.InlineMediaIndex)
                                          .Select(media => media.AttachmentId)
                                          .ToArray();
            var item = CreateItem(batchId, entry, attachmentIds);
            try
            {
                foreach (var media in plan.Media.Where(media => media.SourceEntryId == entry.SourceEntryId))
                {
                    var bytes = await _mediaReader.ReadAsync(source, media, cancellationToken);
                    var hash  = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    if (!hash.Equals(media.ContentSha256, StringComparison.OrdinalIgnoreCase))
                        throw new TtrImportValidationException($"Media hash changed for source entry {entry.SourceEntryId}, media {media.SourceMediaId?.ToString() ?? $"inline-{media.InlineMediaIndex}"}.");
                    await using var stream = new MemoryStream(bytes, writable: false);
                    await _mediaService.AddImportedAttachmentAsync(media.AttachmentId
                                                                 , "JournalEntry"
                                                                 , entry.EntryId
                                                                 , media.LogicalFileName
                                                                 , media.ContentType
                                                                 , stream
                                                                 , media.SizeBytes
                                                                 , entry.CreatedUtc);
                    await using var storedStream = await _mediaService.GetAttachmentStreamAsync(media.AttachmentId);
                    if (storedStream is null)
                        throw new TtrImportValidationException($"Imported attachment {media.AttachmentId} cannot be opened.");
                    var storedHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(storedStream, cancellationToken));
                    if (!storedHash.Equals(media.ContentSha256, StringComparison.OrdinalIgnoreCase))
                        throw new TtrImportValidationException($"Imported attachment {media.AttachmentId} failed hash verification.");
                }

                var writeResult = await _journalWriter.CreateAsync(new HistoricalJournalWriteRequest
                                                                   {
                                                                       EntryId      = entry.EntryId
                                                                     , RevisionId   = entry.RevisionId
                                                                     , CreatedUtc   = entry.CreatedUtc
                                                                     , PartitionKey = plan.TargetPartition
                                                                     , Text          = entry.NormalizedText
                                                                     , Tags          = entry.Tags
                                                                     , Mood          = entry.Mood
                                                                     , MoodScore     = entry.MoodScore
                                                                     , MoodLevel     = entry.MoodLevel
                                                                     , MediaPaths    = attachmentIds
                                                                   });
                item.Status = writeResult.AlreadyExists ? "AlreadyImported" : "Imported";
                if (writeResult.AlreadyExists) batch.AlreadyImportedCount++;
                else batch.ImportedCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.Status = "Failed";
                item.Error  = exception.Message;
                batch.FailedCount++;
            }
            await _store.Save(item, id: item.Id);
            await _store.Save(batch, id: batch.Id);
        }

        batch.VerificationPassed = batch.FailedCount == 0 && await VerifyAsync(plan, cancellationToken);
        batch.Status             = batch.VerificationPassed ? "Completed" : "Failed";
        batch.CompletedUtc       = DateTimeOffset.UtcNow;
        await _store.Save(batch, id: batch.Id);
        return new TtrImportExecutionResult(batch.Id, batch.ImportedCount, batch.AlreadyImportedCount, batch.FailedCount, batch.VerificationPassed);
    }

    private async Task<bool> VerifyAsync(TtrImportPlan plan, CancellationToken cancellationToken)
    {
        foreach (var entry in plan.Entries.Where(item => item.Disposition == "Ready"))
        {
            var savedEntry    = _store.Get<JournalEntry>(entry.EntryId, plan.TargetPartition);
            var savedRevision = _store.Get<JournalRevision>(entry.RevisionId, plan.TargetPartition);
            if (savedEntry?.CreatedUtc != entry.CreatedUtc
             || savedRevision?.EntryId != entry.EntryId
             || savedRevision.CreatedUtc != entry.CreatedUtc
             || savedRevision.Text != entry.NormalizedText
             || savedRevision.State != JournalEntryState.Committed)
                return false;
        }
        foreach (var media in plan.Media.Where(item => item.Disposition == "Ready"))
        {
            await using var stream = await _mediaService.GetAttachmentStreamAsync(media.AttachmentId);
            if (stream is null) return false;
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(media.ContentSha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private async Task ValidateExecutionAsync(TtrImportPlanRequest source
                                             , TtrImportPlan plan
                                             , TtrImportExecutionRequest request
                                             , CancellationToken cancellationToken)
    {
        if (!File.Exists(source.DatabasePath))
            throw new TtrImportValidationException("The source database no longer exists.");
        await using var sourceStream = new FileStream(source.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var currentSourceHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(sourceStream, cancellationToken));
        if (!currentSourceHash.Equals(plan.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException("The source database changed after planning.");
        if (!source.ExpectedDatabaseSha256.Equals(plan.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException("The plan source hash does not match the requested source hash.");
        if (!request.ReviewedPlanSha256.Equals(plan.PlanSha256, StringComparison.OrdinalIgnoreCase)
         || !TtrImportPlanner.ComputePlanHash(plan).Equals(plan.PlanSha256, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException("The reviewed plan hash does not match the executable plan.");
        if (plan.Findings.Any(finding => finding.IsBlocking) || plan.Entries.Any(entry => entry.Disposition == "Blocked"))
            throw new TtrImportValidationException("Execution is blocked because the plan contains blocking findings.");
        if (!request.TargetEnvironment.Equals(_targetEnvironment, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException($"Target environment mismatch. Expected {_targetEnvironment}.");
        if (!Path.GetFullPath(request.TargetDatabasePath).Equals(_targetDatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException("Target database path does not match the configured object store database.");
        if (!File.Exists(request.BackupManifestPath))
            throw new TtrImportValidationException("A fresh destination backup manifest is required.");
        await using var manifest = File.OpenRead(request.BackupManifestPath);
        var manifestHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(manifest, cancellationToken));
        if (!manifestHash.Equals(request.BackupManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new TtrImportValidationException("Backup manifest SHA-256 mismatch.");
    }

    private static TtrImportItem CreateItem(string batchId, TtrPlannedEntry entry, IReadOnlyList<string> attachmentIds)
    {
        return new TtrImportItem
               {
                   Id                      = TtrDeterministicIdentity.CreateItemId(batchId, entry.SourceEntryId).ToString()
                 , BatchId                 = batchId
                 , SourceEntryId           = entry.SourceEntryId
                 , OriginalJournalId       = entry.OriginalJournalId
                 , EntryId                 = entry.EntryId
                 , RevisionId              = entry.RevisionId
                 , SourceContentSha256     = entry.SourceContentSha256
                 , NormalizedContentSha256 = entry.NormalizedContentSha256
                 , SourceJournalTitle      = entry.SourceJournalTitle
                 , SourceJournalTypeTitle  = entry.SourceJournalTypeTitle
                 , SourceMood              = entry.Mood
                 , AttachmentIds           = attachmentIds
               };
    }
}
