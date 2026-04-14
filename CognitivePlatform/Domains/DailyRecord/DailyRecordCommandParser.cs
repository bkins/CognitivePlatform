using System.Text;
using System.Text.RegularExpressions;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.DailyRecord;

/// <summary>
/// Parses the three daily-record command prefixes:
///   Plan:    → DailyCommandType.Plan    (morning intention + optional Tasks: block)
///   Check:   → DailyCommandType.Check   (intraday check-in + optional Tasks: block)
///   EOD: / Done: / Evening: / DayDone:
///            → DailyCommandType.EndOfDay (evening reflection)
///
/// Tags:, Mood:, and MoodScore: are parsed identically to JournalCommandParser.
/// Tasks: accepts either an inline comma-separated list or a multi-line bullet list.
/// </summary>
public sealed class DailyRecordCommandParser : IDailyRecordCommandParser
{
    private static readonly Regex QuotedValueRegex = new(@"""([^""]+)""", RegexOptions.Compiled);

    // Ordered from most-specific to least-specific to avoid prefix collisions.
    private static readonly (string Prefix, DailyCommandType Type)[] PrefixMap =
    {
            ("daydone:", DailyCommandType.EndOfDay)
          , ("evening:", DailyCommandType.EndOfDay)
          , ("check:",   DailyCommandType.Check)
          , ("plan:",    DailyCommandType.Plan)
          , ("done:",    DailyCommandType.EndOfDay)
          , ("eod:",     DailyCommandType.EndOfDay)
    };

    private static readonly string[] MetaDirectives = { "Tags:", "Mood:", "MoodScore:" };

    // -------------------------------------------------------------------------

    public ParsedDailyCommand Parse(string input)
    {
        if (input.HasValue().Not())
            return new ParsedDailyCommand();

        var lines = input.Split('\n')
                         .Select(line => line.Trim())
                         .ToList();

        var (commandType, bodyOnFirstLine) = DetectPrefix(lines[0]);

        if (commandType == DailyCommandType.Unknown)
            return new ParsedDailyCommand();

        // If the user typed everything on one line (e.g. Enter submits in the chat UI),
        // directives like "Tasks:" may be embedded in the first-line body rather than on
        // their own lines.  Split them out so the normal line-processing loop handles them.
        var (cleanFirstLineBody, syntheticDirectiveLines) = ExtractInlineDirectives(bodyOnFirstLine);

        var bodyBuilder = new StringBuilder(cleanFirstLineBody);
        var tasks       = new List<string>();
        var tags        = new List<string>();
        string? mood    = null;
        int?    moodScore = null;
        bool inTasksBlock = false;

        // Merge synthetic directive "lines" (extracted from first-line body) with
        // the real subsequent lines so a single unified loop handles both.
        var allRemainingLines = syntheticDirectiveLines.Concat(lines.Skip(1)).ToList();

        for (var index = 0; index < allRemainingLines.Count; index++)
        {
            var line = allRemainingLines[index];

            if (line.Length == 0)
            {
                inTasksBlock = false;
                continue;
            }

            // Tasks: — inline or block header
            if (line.StartsWith("Tasks:", StringComparison.OrdinalIgnoreCase))
            {
                var inlinePart = line.Substring("Tasks:".Length).Trim();
                if (inlinePart.Length > 0)
                {
                    tasks.AddRange(SplitCommaSeparated(inlinePart));
                }
                else
                {
                    inTasksBlock = true;
                }
                continue;
            }

            // Bullet list items inside a Tasks: block
            if (inTasksBlock && (line.StartsWith("- ") || line.StartsWith("* ")))
            {
                var title = line.Substring(2).Trim();
                if (title.Length > 0) tasks.Add(title);
                continue;
            }

            inTasksBlock = false;

            // Tags:
            var tagsSegment = ExtractDirectiveSegment(line, "Tags:");
            if (tagsSegment != null)
            {
                tags.AddRange(HasQuotedValues(tagsSegment)
                                  ? ExtractQuotedValues(tagsSegment)
                                  : SplitCommaSeparated(tagsSegment));
                continue;
            }

            // Mood:
            var moodSegment = ExtractDirectiveSegment(line, "Mood:");
            if (moodSegment != null)
            {
                mood = HasQuotedValues(moodSegment)
                           ? ExtractQuotedValues(moodSegment).FirstOrDefault()
                           : moodSegment.Trim() is { Length: > 0 } unquoted ? unquoted : null;
                continue;
            }

            // MoodScore:
            var moodScoreSegment = ExtractDirectiveSegment(line, "MoodScore:");
            if (moodScoreSegment != null)
            {
                moodScore = ExtractIntValue(moodScoreSegment);
                continue;
            }

            // Body text continuation
            if (bodyBuilder.Length > 0)
                bodyBuilder.Append(' ');

            bodyBuilder.Append(line);
        }

        return new ParsedDailyCommand
               {
                       CommandType = commandType
                     , BodyText    = bodyBuilder.ToString().Trim()
                     , Tasks       = tasks
                     , Tags        = tags
                     , Mood        = mood
                     , MoodScore   = moodScore
               };
    }

    /// <summary>
    /// Returns true if the input starts with a recognised daily-record prefix,
    /// without performing a full parse. Used by FastPathResolver for fast screening.
    /// </summary>
    public static bool StartsWithKnownPrefix(string input)
    {
        if (input.HasValue().Not()) return false;

        var lower = input.TrimStart().ToLowerInvariant();

        foreach (var (prefix, _) in PrefixMap)
        {
            if (lower.StartsWith(prefix)) return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Private helpers

    /// <summary>
    /// Splits any directive keywords (Tasks:, Tags:, Mood:, MoodScore:) that are embedded
    /// inline within the first-line body.  This handles the common case where the user types
    /// everything on one line because Enter submits the chat input.
    ///
    /// E.g. "Focused day. Tasks: Fix bug, Write tests Mood: Calm"
    ///   → cleanBody = "Focused day."
    ///   → directiveLines = [ "Tasks: Fix bug, Write tests", "Mood: Calm" ]
    /// </summary>
    private static (string CleanBody, IReadOnlyList<string> DirectiveLines) ExtractInlineDirectives(string text)
    {
        var allDirectives = new[] { "Tasks:", "Tags:", "Mood:", "MoodScore:" };

        // Find the position of the earliest directive keyword in the text.
        var firstDirectiveIdx = text.Length;

        foreach (var directive in allDirectives)
        {
            var idx = text.IndexOf(directive, StringComparison.OrdinalIgnoreCase);

            if (idx >= 0 && idx < firstDirectiveIdx)
                firstDirectiveIdx = idx;
        }

        // No inline directives found — return text unchanged.
        if (firstDirectiveIdx == text.Length)
            return (text, Array.Empty<string>());

        var cleanBody      = text.Substring(0, firstDirectiveIdx).Trim();
        var directivePart  = text.Substring(firstDirectiveIdx);
        var directiveLines = new List<string>();

        // Walk through the directive portion, splitting at each subsequent directive keyword.
        while (directivePart.Length > 0)
        {
            var nextBoundary = directivePart.Length;

            foreach (var directive in allDirectives)
            {
                // Start at index 1 to skip the directive that begins at position 0.
                var idx = directivePart.IndexOf(directive, 1, StringComparison.OrdinalIgnoreCase);

                if (idx > 0 && idx < nextBoundary)
                    nextBoundary = idx;
            }

            directiveLines.Add(directivePart.Substring(0, nextBoundary).Trim());
            directivePart = directivePart.Substring(nextBoundary);
        }

        return (cleanBody, directiveLines);
    }

    private static (DailyCommandType Type, string BodyText) DetectPrefix(string firstLine)
    {
        var lower = firstLine.ToLowerInvariant();

        foreach (var (prefix, commandType) in PrefixMap)
        {
            if (!lower.StartsWith(prefix)) continue;

            var body = firstLine.Substring(prefix.Length).Trim();
            return (commandType, body);
        }

        return (DailyCommandType.Unknown, string.Empty);
    }

    private static string? ExtractDirectiveSegment(string line, string directive)
    {
        var start = line.IndexOf(directive, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += directive.Length;
        var end = line.Length;

        foreach (var other in MetaDirectives)
        {
            if (other.Equals(directive, StringComparison.OrdinalIgnoreCase)) continue;

            var idx = line.IndexOf(other, start, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < end) end = idx;
        }

        return line.Substring(start, end - start);
    }

    private static bool HasQuotedValues(string segment)
        => QuotedValueRegex.IsMatch(segment);

    private static IEnumerable<string> ExtractQuotedValues(string input)
    {
        foreach (Match match in QuotedValueRegex.Matches(input))
        {
            var value = match.Groups[1].Value.Trim();
            if (value.Length > 0) yield return value;
        }
    }

    private static IEnumerable<string> SplitCommaSeparated(string input)
        => input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0);

    private static int? ExtractIntValue(string input)
    {
        if (!int.TryParse(input.Trim(), out var score))
            return null;

        return score is >= 1 and <= 5 ? score : null;
    }
}
