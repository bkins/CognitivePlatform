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

        var bodyBuilder = new StringBuilder(bodyOnFirstLine);
        var tasks       = new List<string>();
        var tags        = new List<string>();
        string? mood    = null;
        int?    moodScore = null;
        bool inTasksBlock = false;

        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index];

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
