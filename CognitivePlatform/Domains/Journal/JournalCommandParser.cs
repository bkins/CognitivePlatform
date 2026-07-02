using System.Text;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CP.Shared.Primitives.Avails;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalCommandParser : IJournalCommandParser
{
    private static readonly Regex QuotedValueRegex = new(RegexMatchingPatterns.DoubleQuotedStringPattern
                                                       , RegexOptions.Compiled);

    private static readonly string[] Directives =
    {
            "Tags:"
          , "Mood:"
          , "MoodScore:"

            //Later add "Context:", "Media:"
    };
    
    private static bool HasQuotedValues(string segment) => ExtractQuotedValues(segment).Any();
    
    public ParsedJournalCommand Parse(string input)
    {
        if (input.HasValue()
                 .Not())
        {
            return new ParsedJournalCommand
                   {
                           Text = string.Empty
                   };
        }

        var lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(line => line.Trim())
                         .Where(line => line.Length > 0)
                         .ToList();

        var     textBuilder = new StringBuilder();
        var     tags        = new List<string>();
        string? mood        = null;
        int?    moodScore   = null;
        
        foreach (var line in lines)
        {
            var tagsCountBefore = tags.Count;
            var moodBefore      = mood;

            var tagsSegment = ExtractDirectiveSegment(line, "Tags:");
            if (tagsSegment != null)
            {
                tags.AddRange(HasQuotedValues(tagsSegment)
                                      ? ExtractQuotedValues(tagsSegment)
                                      : ExtractUnquotedValues(tagsSegment));
            }

            var moodSegment = ExtractDirectiveSegment(line, "Mood:");
            if (moodSegment != null)
            {
                if (HasQuotedValues(moodSegment))
                    mood = ExtractQuotedValues(moodSegment).FirstOrDefault();
                else if (moodSegment.Trim() is { Length: > 0 } unquotedMood)
                    mood = unquotedMood;
            }

            var moodScoreSegment = ExtractDirectiveSegment(line, "MoodScore:");
            if (moodScoreSegment != null)
                moodScore = ExtractIntValue(moodScoreSegment);

            // Only strip directives from text if they were actually parsed on this line
            var cleanedLine    = line;
            var tagsWereParsed = tags.Count > tagsCountBefore;
            var moodWasParsed  = mood != moodBefore;

            if (tagsWereParsed)
                cleanedLine = RemoveDirectiveContent(cleanedLine, "Tags:");

            if (moodWasParsed)
                cleanedLine = RemoveDirectiveContent(cleanedLine, "Mood:");

            if (moodScoreSegment != null && moodScore.HasValue)
                cleanedLine = RemoveDirectiveContent(cleanedLine, "MoodScore:");

            if (cleanedLine.HasNoValue())
                continue;

            if (textBuilder.Length > 0)
                textBuilder.AppendLine();

            textBuilder.Append(cleanedLine.Trim());
        }

        return new ParsedJournalCommand
               {
                       Text      = textBuilder.ToString()
                     , Tags      = tags
                     , Mood      = mood
                     , MoodScore = moodScore
               };
    }

    private static IEnumerable<string> ExtractQuotedValues(string input)
    {
        foreach (Match match in QuotedValueRegex.Matches(input))
        {
            var value = match.Groups[1].Value.Trim();

            if (value.Length > 0) yield return value;
        }
    }

    private static IEnumerable<string> ExtractUnquotedValues(string input)
        => input.Split(',')
                .Select(value => value.Trim())
                .Where(value => value.Length > 0);
    
    private static int? ExtractIntValue(string input)
    {
        if (!int.TryParse(input.Trim(), out var score))
            return null;

        if (score is >= 1 and <= 5)
            return score;

        return null;
    }
    
    private static string? ExtractDirectiveSegment(string line, string directive)
    {
        var start = line.IndexOf(directive, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += directive.Length;

        var end = line.Length;

        foreach (var other in Directives)
        {
            if (other.Equals(directive, StringComparison.OrdinalIgnoreCase))
                continue;

            var idx = line.IndexOf(other, start, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < end)
                end = idx;
        }

        return line.Substring(start, end - start);
    }

    private static string RemoveDirectiveContent(string line, string directive)
    {
        var start = line.IndexOf(directive, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return line;

        var contentStart = start + directive.Length;
        var end          = line.Length;

        foreach (var other in Directives)
        {
            if (other.Equals(directive, StringComparison.OrdinalIgnoreCase)) continue;

            var idx = line.IndexOf(other, contentStart, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < end) end = idx;
        }

        return (line[..start] + line[end..]).Trim();
    }

}
