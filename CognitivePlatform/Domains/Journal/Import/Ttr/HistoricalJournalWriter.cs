using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class HistoricalJournalWriter : IHistoricalJournalWriter
{
    private readonly IObjectStore             _store;
    private readonly IHistoricalObjectWriter _historicalWriter;

    public HistoricalJournalWriter(IObjectStore store, IHistoricalObjectWriter historicalWriter)
    {
        _store            = store;
        _historicalWriter = historicalWriter;
    }

    public async Task<HistoricalJournalWriteResult> CreateAsync(HistoricalJournalWriteRequest request)
    {
        Validate(request);
        var existingEntry    = _store.Get<JournalEntry>(request.EntryId, request.PartitionKey);
        var existingRevision = _store.Get<JournalRevision>(request.RevisionId, request.PartitionKey);
        if (existingEntry is not null && !EntryMatches(existingEntry, request))
            throw new InvalidOperationException($"Historical journal identity collision for entry {request.EntryId}.");
        if (existingRevision is not null && !RevisionMatches(existingRevision, request))
            throw new InvalidOperationException($"Historical journal identity collision for revision {request.RevisionId}.");
        if (existingEntry is not null && existingRevision is not null)
            return new HistoricalJournalWriteResult(true);

        var entry = new JournalEntry
                    {
                        Id         = request.EntryId
                      , CreatedUtc = request.CreatedUtc
                    };
        var revision = new JournalRevision
                       {
                           RevisionId = request.RevisionId
                         , EntryId    = request.EntryId
                         , CreatedUtc = request.CreatedUtc
                         , Text       = request.Text
                         , Tags       = request.Tags
                         , Mood       = request.Mood
                         , MoodScore  = request.MoodScore
                         , MoodLevel  = request.MoodLevel
                         , MediaPaths = request.MediaPaths
                         , State      = JournalEntryState.Committed
                       };
        if (existingEntry is null)
            await _historicalWriter.SaveHistorical(entry, request.CreatedUtc, request.PartitionKey, request.EntryId);
        if (existingRevision is null)
            await _historicalWriter.SaveHistorical(revision, request.CreatedUtc, request.PartitionKey, request.RevisionId);
        return new HistoricalJournalWriteResult(false);
    }

    private static bool EntryMatches(JournalEntry entry, HistoricalJournalWriteRequest request)
    {
        return entry.CreatedUtc == request.CreatedUtc;
    }

    private static bool RevisionMatches(JournalRevision revision, HistoricalJournalWriteRequest request)
    {
        return revision.EntryId == request.EntryId
            && revision.CreatedUtc == request.CreatedUtc
            && revision.Text == request.Text
            && revision.Tags.SequenceEqual(request.Tags)
            && revision.Mood == request.Mood
            && revision.MoodScore == request.MoodScore
            && revision.MoodLevel == request.MoodLevel
            && revision.MediaPaths.SequenceEqual(request.MediaPaths)
            && revision.State == JournalEntryState.Committed;
    }

    private static void Validate(HistoricalJournalWriteRequest request)
    {
        if (!Guid.TryParse(request.EntryId, out _)) throw new ArgumentException("Entry ID must be a GUID.", nameof(request));
        if (!Guid.TryParse(request.RevisionId, out _)) throw new ArgumentException("Revision ID must be a GUID.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PartitionKey)) throw new ArgumentException("Partition key is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Normalized journal text is required.", nameof(request));
    }
}
