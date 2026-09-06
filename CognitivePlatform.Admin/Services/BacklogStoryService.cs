using System.Text.RegularExpressions;

namespace CognitivePlatform.Admin.Services;

public sealed class BacklogStoryService
{
    private static readonly Regex EnhancementIdPattern = new(@"\bENH-(?<number>\d+)\b", RegexOptions.Compiled);
    private static readonly SemaphoreSlim WriteLock     = new(1, 1);

    private readonly IBacklogBoardCompiler _compiler;
    private readonly IReadOnlyDictionary<string, string> _backlogPathsByProject;

    public BacklogStoryService(BacklogStoryOptions options, IBacklogBoardCompiler compiler)
    {
        _backlogPathsByProject = options.BacklogPathsByProject;
        _compiler              = compiler;
    }

    public async Task<BacklogStoryResult> AddStoryAsync(
        AddBacklogStoryRequest request
      , CancellationToken     cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return BacklogStoryResult.Failure(validationError);
        }

        if (!_backlogPathsByProject.TryGetValue(request.ProjectName.Trim(), out var backlogPath) ||
            string.IsNullOrWhiteSpace(backlogPath))
        {
            return BacklogStoryResult.Failure("The selected project is not configured for story creation.");
        }

        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var originalBacklog = await File.ReadAllTextAsync(backlogPath, cancellationToken);
            var storyId         = GetNextEnhancementId(originalBacklog);
            var storyRow        = BuildStoryRow(storyId, request);
            var updatedBacklog  = AppendToEnhancementsSection(originalBacklog, storyRow);

            if (updatedBacklog is null)
            {
                return BacklogStoryResult.Failure("The configured backlog has no Enhancements section to receive this story.");
            }

            await File.WriteAllTextAsync(backlogPath, updatedBacklog, cancellationToken);
            await _compiler.CompileAsync(cancellationToken);

            return BacklogStoryResult.Success(storyId);
        }
        catch (FileNotFoundException)
        {
            return BacklogStoryResult.Failure("The configured backlog source file could not be found.");
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static string? Validate(AddBacklogStoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectName) ||
            string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Description) ||
            string.IsNullOrWhiteSpace(request.Area))
        {
            return "Project, title, description, and area are required.";
        }

        if (request.Title.Length > 240 || request.Description.Length > 4_000 || request.Area.Length > 240)
        {
            return "Title, description, or area is too long.";
        }

        if (string.Equals(request.Status, "Planned", StringComparison.OrdinalIgnoreCase).Not() &&
            string.Equals(request.Status, "In Progress", StringComparison.OrdinalIgnoreCase).Not())
        {
            return "Status must be Planned or In Progress.";
        }

        return null;
    }

    private static string GetNextEnhancementId(string originalBacklog)
    {
        var largestExistingNumber = EnhancementIdPattern.Matches(originalBacklog)
                                                      .Select(match => int.Parse(match.Groups["number"].Value))
                                                      .DefaultIfEmpty(0)
                                                      .Max();

        return $"ENH-{largestExistingNumber + 1}";
    }

    private static string BuildStoryRow(string storyId, AddBacklogStoryRequest request)
    {
        var title       = SanitizeTableCell(request.Title);
        var description = SanitizeTableCell(request.Description);
        var area        = SanitizeTableCell(request.Area);
        var status      = request.Status.Trim().Equals("In Progress", StringComparison.OrdinalIgnoreCase)
            ? "In Progress"
            : "Planned";

        return $"| {storyId} | **{title}** — {description} | {area} | {status} |";
    }

    private static string? AppendToEnhancementsSection(string originalBacklog, string storyRow)
    {
        var sectionMatch = Regex.Match(originalBacklog, @"(?m)^## Enhancements\s*$");
        if (sectionMatch.Success.Not())
        {
            return null;
        }

        var remainderStart   = sectionMatch.Index + sectionMatch.Length;
        var nextSectionMatch = Regex.Match(originalBacklog[remainderStart..], @"(?m)^## ");
        var insertionIndex   = nextSectionMatch.Success ? remainderStart + nextSectionMatch.Index : originalBacklog.Length;
        var prefix           = originalBacklog[..insertionIndex].TrimEnd();
        var suffix           = originalBacklog[insertionIndex..];

        return $"{prefix}{Environment.NewLine}{storyRow}{Environment.NewLine}{Environment.NewLine}{suffix}";
    }

    private static string SanitizeTableCell(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", " ")
                    .Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("|", "\\|");
    }
}
