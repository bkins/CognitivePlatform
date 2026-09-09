using System.Text.RegularExpressions;

namespace CognitivePlatform.Api.Domains.Backlog;

public sealed class LegacyBacklogImporter
{
    private readonly IBacklogBoardService _service;

    public LegacyBacklogImporter(IBacklogBoardService service)
    {
        _service = service;
    }

    public async Task<BacklogMigrationReport> ImportAsync( string             projectKey
                                                           , string             sourcePath
                                                           , CancellationToken  cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new BacklogValidationException("The legacy backlog source was not found.");

        var lines      = await File.ReadAllLinesAsync(sourcePath, cancellationToken);
        var board       = await _service.GetBoardAsync(cancellationToken);
        var knownAreas  = board.Areas.Select(area => area.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownIds    = board.Stories.Select(story => story.DisplayId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exceptions  = new List<string>();
        var sourceRows  = 0;
        var imported    = 0;
        var skipped     = 0;
        var section     = "story";

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = GetItemType(line);
                continue;
            }

            if (!line.StartsWith('|') || line.Contains("---")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 4 || cells[0].Equals("ID", StringComparison.OrdinalIgnoreCase)) continue;
            sourceRows++;

            var displayId = cells[0].Replace("~~", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(displayId) || knownIds.Contains(displayId)) { skipped++; continue; }
            // Legacy tables are not consistently pipe-escaped.  The schema
            // guarantees the ID first and the Area/Status pair last, so retain
            // every intervening fragment as the description.
            var description = string.Join(" | ", cells[1..^2]);
            var areaName = cells[^2];
            var status = cells[^1];
            var areaKey = ToKey(areaName);
            if (knownAreas.Add(areaKey))
            {
                try { await _service.CreateReferenceAsync("area", new CreateBacklogReferenceRequest(areaKey, areaName, knownAreas.Count, "legacy-import"), cancellationToken); }
                catch (BacklogValidationException) { }
            }

            try
            {
                var (title, detail) = SplitTitle(description);
                await _service.CreateStoryAsync(new CreateBacklogStoryRequest(
                    projectKey
                  , areaKey
                  , section
                  , GetColumn(status)
                  , title
                  , detail
                  , ExtractPriority(description)
                  , null
                  , new Dictionary<string, string> { ["legacy-id"] = displayId, ["legacy-status"] = status }
                  , "legacy-import"
                  , displayId), cancellationToken);
                knownIds.Add(displayId);
                imported++;
            }
            catch (Exception exception)
            {
                exceptions.Add($"{displayId}: {exception.Message}");
            }
        }

        return new BacklogMigrationReport(sourceRows, imported, skipped, exceptions, exceptions.Count == 0 && sourceRows == imported + skipped);
    }

    private static string GetItemType(string heading)
    {
        var value = heading.ToLowerInvariant();
        if (value.Contains("bug")) return "bug";
        if (value.Contains("ux")) return "ux";
        if (value.Contains("technical debt") || value.Contains("performance")) return "technical-debt";
        if (value.Contains("enhancement")) return "enhancement";
        return "story";
    }

    private static string GetColumn(string status)
    {
        var value = status.ToLowerInvariant();
        if (value.Contains("complete") || value.Contains("fixed") || value.Contains("done")) return "done";
        if (value.Contains("progress") || value.Contains("sprint")) return "in-progress";
        if (value.Contains("planned")) return "planned";
        return "backlog";
    }

    private static (string Title, string Detail) SplitTitle(string value)
    {
        var clean = Regex.Replace(value, @"\*\*", string.Empty).Trim();
        var parts = clean.Split("—", 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (clean, clean);
    }

    private static int ExtractPriority(string value)
    {
        var match = Regex.Match(value, @"priority:\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var priority) ? Math.Clamp(priority, 1, 99) : 50;
    }

    private static string ToKey(string value)
    {
        var key = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(key) ? "unassigned" : key;
    }
}
