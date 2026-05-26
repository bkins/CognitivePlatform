using System.Text;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Capabilities;

namespace CognitivePlatform.Api.Domains.Journal.Capabilities;

/// <summary>
/// Adapts IJournalService to ICrudService&lt;JournalEntryWithRevision&gt; so that
/// the generic CrudCapabilityTemplate can drive journal operations without
/// depending on journal-specific types.
///
/// Standard list format: [{date:yyyy-MM-dd HH:mm}] {text} (ID: {id})
/// </summary>
public sealed class JournalCrudServiceAdapter : ICrudService<JournalEntryWithRevision>
{
    private readonly IJournalService _journalService;

    public JournalCrudServiceAdapter(IJournalService journalService)
    {
        _journalService = journalService;
    }

    public async Task<JournalEntryWithRevision> AddAsync( IDictionary<string, string> parameters
                                                        , CancellationToken           cancellationToken )
    {
        var text = parameters.TryGetValue("text", out var rawText) ? rawText : string.Empty;
        var mood = parameters.TryGetValue("mood", out var rawMood) ? rawMood : null;

        var entryId = await _journalService.AddEntryAsync(text:       text
                                                        , tags:       Array.Empty<string>()
                                                        , mood:       mood
                                                        , moodScore:  null
                                                        , moodLevel:  null
                                                        , mediaPaths: Array.Empty<string>());

        return _journalService.GetById(entryId);
    }

    public Task<IReadOnlyList<JournalEntryWithRevision>> ListAsync(CancellationToken cancellationToken)
    {
        var entries = _journalService.ListEntries();
        return Task.FromResult<IReadOnlyList<JournalEntryWithRevision>>(entries);
    }

    public Task<JournalEntryWithRevision?> GetAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var entry = _journalService.GetById(id);
            return Task.FromResult<JournalEntryWithRevision?>(entry);
        }
        catch (KeyNotFoundException)
        {
            return Task.FromResult<JournalEntryWithRevision?>(null);
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = _journalService.DeleteEntry(id, "Deleted via generated action.");
            return Task.FromResult(deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Standard list format: [{date:yyyy-MM-dd HH:mm}] {text} (ID: {id})
    /// </summary>
    public string FormatForList(JournalEntryWithRevision entity)
        => $"[{entity.Entry.CreatedUtc:yyyy-MM-dd HH:mm}] {entity.LatestRevision.Text} (ID: {entity.Entry.Id})";

    public string FormatForDetail(JournalEntryWithRevision entity)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{entity.Entry.CreatedUtc:yyyy-MM-dd HH:mm}] {entity.LatestRevision.Text}");
        sb.AppendLine($"ID: {entity.Entry.Id}");

        if (entity.LatestRevision.Mood is not null)
            sb.AppendLine($"Mood: {entity.LatestRevision.Mood}");

        if (entity.LatestRevision.Tags.Count > 0)
            sb.AppendLine($"Tags: {string.Join(", ", entity.LatestRevision.Tags)}");

        return sb.ToString().TrimEnd();
    }
}
