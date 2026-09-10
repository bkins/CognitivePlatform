using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Backlog;

public sealed class LegacyBacklogReconciliationService
{
    public const string CognitivePlatformSourcePath = @"C:\Users\benho\source\Application Documentation\The CP Universe\Documentation\BACKLOG.original.md";

    private readonly IBacklogBoardService _service;
    private readonly string _sourcePath;

    public LegacyBacklogReconciliationService( IBacklogBoardService service
                                              , string               sourcePath = CognitivePlatformSourcePath)
    {
        _service = service;
        _sourcePath = sourcePath;
    }

    public async Task<LegacyReconciliationPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var source = await ReadSourceAsync(cancellationToken);
        var board = await _service.GetBoardAsync(cancellationToken);
        var sourceById = source.Rows.ToDictionary(row => row.DisplayId, StringComparer.OrdinalIgnoreCase);
        var stories = new List<LegacyReconciliationStoryPreview>();

        foreach (var story in board.Stories)
        {
            var legacyId = story.Properties.TryGetValue("legacy-id", out var storedLegacyId)
                ? storedLegacyId
                : story.DisplayId;
            if (!sourceById.TryGetValue(legacyId, out var legacy)) continue;

            var resolvedStreamKey = ResolveStreamKey(legacy.StreamName, board.Streams);
            var streamDiffers = !resolvedStreamKey.EqualsIgnoreCase(story.StreamKey);
            var titleDiffers = !string.Equals(legacy.Title, story.Title, StringComparison.Ordinal);
            var descriptionDiffers = !string.Equals(legacy.Description, story.Description, StringComparison.Ordinal);
            if (legacy.Priority == story.Priority
                && !streamDiffers
                && legacy.ColumnKey.EqualsIgnoreCase(story.ColumnKey)
                && !titleDiffers
                && !descriptionDiffers) continue;

            stories.Add(new LegacyReconciliationStoryPreview(
                story.Id,
                story.DisplayId,
                story.Revision,
                legacy.Priority,
                story.Priority,
                legacy.StreamName,
                story.StreamKey,
                streamDiffers,
                legacy.ColumnKey,
                story.ColumnKey,
                titleDiffers,
                descriptionDiffers));
        }

        var boardOnly = board.Stories
                             .Where(story => !sourceById.ContainsKey(story.Properties.TryGetValue("legacy-id", out var legacyId) ? legacyId : story.DisplayId))
                             .Select(story => story.DisplayId)
                             .OrderBy(displayId => displayId, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        return new LegacyReconciliationPreview(source.Fingerprint, stories, boardOnly);
    }

    public async Task<LegacyReconciliationApplyResult> ApplyAsync( LegacyReconciliationApplyRequest request
                                                                    , CancellationToken              cancellationToken = default)
    {
        if (request.Selections.Count == 0) throw new BacklogValidationException("Select at least one reconciliation field to apply.");

        var source = await ReadSourceAsync(cancellationToken);
        if (!string.Equals(source.Fingerprint, request.SourceFingerprint, StringComparison.Ordinal))
            throw new BacklogConflictException("The legacy source changed after preview. Generate a fresh preview before applying changes.");

        var board = await _service.GetBoardAsync(cancellationToken);
        var sourceById = source.Rows.ToDictionary(row => row.DisplayId, StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<LegacyReconciliationApplyOutcome>();
        var createdStreamKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selection in request.Selections.Where(selection => selection.ApplyPriority || selection.ApplyStream || selection.ApplyColumn || selection.ApplyTitle || selection.ApplyDescription))
        {
            var story = board.Stories.FirstOrDefault(candidate => candidate.Id == selection.StoryId);
            if (story is null)
            {
                outcomes.Add(new LegacyReconciliationApplyOutcome(selection.StoryId.ToString(), "skipped", "The story is no longer active."));
                continue;
            }

            if (story.Revision != selection.ExpectedRevision)
            {
                outcomes.Add(new LegacyReconciliationApplyOutcome(story.DisplayId, "skipped", "The story changed after preview."));
                continue;
            }

            var legacyId = story.Properties.TryGetValue("legacy-id", out var storedLegacyId) ? storedLegacyId : story.DisplayId;
            if (!sourceById.TryGetValue(legacyId, out var legacy))
            {
                outcomes.Add(new LegacyReconciliationApplyOutcome(story.DisplayId, "skipped", "No current legacy row matches this story."));
                continue;
            }

            try
            {
                var streamKey = story.StreamKey;
                if (selection.ApplyStream)
                    streamKey = await ResolveOrCreateStreamAsync(legacy.StreamName, board, createdStreamKeys, request.Actor, cancellationToken);

                var updated = story;
                if (selection.ApplyPriority || selection.ApplyStream || selection.ApplyTitle || selection.ApplyDescription)
                {
                    updated = await _service.UpdateStoryAsync(story.Id, new UpdateBacklogStoryRequest(
                        selection.ApplyTitle ? legacy.Title : story.Title,
                        selection.ApplyDescription ? legacy.Description : story.Description,
                        story.ProjectKey,
                        story.AreaKey,
                        story.ItemTypeKey,
                        selection.ApplyPriority ? legacy.Priority : story.Priority,
                        streamKey,
                        story.Properties,
                        story.Revision,
                        request.Actor ?? "legacy-reconciliation"), cancellationToken);
                }

                if (selection.ApplyColumn)
                {
                    updated = await _service.MoveStoryAsync(story.Id, new MoveBacklogStoryRequest(
                        legacy.ColumnKey,
                        null,
                        null,
                        updated.Revision,
                        request.Actor ?? "legacy-reconciliation"), cancellationToken);
                }

                outcomes.Add(new LegacyReconciliationApplyOutcome(updated.DisplayId, "applied", "Selected legacy metadata applied to the updated board."));
            }
            catch (BacklogConflictException)
            {
                outcomes.Add(new LegacyReconciliationApplyOutcome(story.DisplayId, "skipped", "The story changed while reconciliation was applying."));
            }
            catch (BacklogValidationException exception)
            {
                outcomes.Add(new LegacyReconciliationApplyOutcome(story.DisplayId, "skipped", exception.Message));
            }
        }

        return new LegacyReconciliationApplyResult(outcomes);
    }

    private async Task<string?> ResolveOrCreateStreamAsync( string?                            streamName
                                                            , BacklogBoardDto                   board
                                                            , IDictionary<string, string>       createdStreamKeys
                                                            , string?                           actor
                                                            , CancellationToken                 cancellationToken)
    {
        var existingKey = ResolveStreamKey(streamName, board.Streams);
        if (existingKey is not null || streamName.HasNoValue()) return existingKey;
        var resolvedStreamName = streamName!;
        if (createdStreamKeys.TryGetValue(resolvedStreamName, out var createdKey)) return createdKey;

        var key = ToKey(resolvedStreamName);
        var nextSortOrder = board.Streams.Count == 0 ? 10 : board.Streams.Max(stream => stream.SortOrder) + 10;
        await _service.CreateReferenceAsync("stream", new CreateBacklogReferenceRequest(key, resolvedStreamName, nextSortOrder, actor ?? "legacy-reconciliation"), cancellationToken);
        createdStreamKeys[resolvedStreamName] = key;
        return key;
    }

    private async Task<LegacySource> ReadSourceAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sourcePath)) throw new BacklogValidationException("The canonical legacy backlog source was not found.");
        var bytes = await File.ReadAllBytesAsync(_sourcePath, cancellationToken);
        var rows = ParseRows(Encoding.UTF8.GetString(bytes));
        return new LegacySource(Convert.ToHexString(SHA256.HashData(bytes)), rows);
    }

    private static IReadOnlyList<LegacyRow> ParseRows(string markdown)
    {
        var rows = new Dictionary<string, LegacyRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in markdown.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (!line.StartsWith('|') || line.Contains("---")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 4 || cells[0].EqualsIgnoreCase("ID")) continue;
            var displayId = cells[0].Replace("~~", string.Empty).Trim();
            if (displayId.HasNoValue() || rows.ContainsKey(displayId)) continue;
            var description = string.Join(" | ", cells[1..^2]);
            var (title, detail) = SplitTitle(description);
            var status = cells[^1];
            rows[displayId] = new LegacyRow(displayId, title, detail, ExtractPriority(description), ExtractStreamName(description), GetColumn(status));
        }
        return rows.Values.ToList();
    }

    private static (string Title, string Detail) SplitTitle(string value)
    {
        var clean = NormalizeStoryText(Regex.Replace(value, @"\*\*", string.Empty));
        var parts = clean.Split("—", 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (clean, clean);
    }

    private static string NormalizeStoryText(string value)
    {
        return Regex.Replace(value, @"\s*<!--\s*priority:\s*\d+\s*-->\s*(?:`?\[[^\]]*\]`?)?\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static int ExtractPriority(string value)
    {
        var match = Regex.Match(value, @"priority:\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var priority) ? Math.Clamp(priority, 1, 99) : 50;
    }

    private static string? ExtractStreamName(string value)
    {
        var match = Regex.Match(value, @"(?:^|\||\[)\s*(Stream\s+[^|\]]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string GetColumn(string status)
    {
        var value = status.ToLowerInvariant();
        if (value.Contains("complete") || value.Contains("fixed") || value.Contains("done")) return "done";
        if (value.Contains("progress") || value.Contains("sprint")) return "in-progress";
        if (value.Contains("planned")) return "planned";
        return "backlog";
    }

    private static string? ResolveStreamKey(string? streamName, IReadOnlyList<BacklogReferenceDto> streams)
    {
        if (streamName.HasNoValue()) return null;
        var resolvedStreamName = streamName!;
        var normalizedKey = ToKey(resolvedStreamName);
        return streams.FirstOrDefault(stream => stream.Name.EqualsIgnoreCase(resolvedStreamName)
                                              || stream.Key.EqualsIgnoreCase(normalizedKey)
                                              || stream.Name.StartsWithIgnoreCase($"{resolvedStreamName} —"))?.Key;
    }

    private static string ToKey(string value)
    {
        var key = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return key.HasNoValue() ? "legacy-stream" : key;
    }

    private sealed record LegacySource(string Fingerprint, IReadOnlyList<LegacyRow> Rows);
    private sealed record LegacyRow(string DisplayId, string Title, string Description, int Priority, string? StreamName, string ColumnKey);
}
