using System.Text;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalCommandParser : IJournalCommandParser
{
    private static readonly Regex QuotedValueRegex = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);

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
            var tagsSegment = ExtractDirectiveSegment(line, "Tags:");
            if (tagsSegment != null 
             && HasQuotedValues(tagsSegment))
            {
                tags.AddRange(ExtractQuotedValues(tagsSegment));
            }

            var moodSegment = ExtractDirectiveSegment(line, "Mood:");
            if (moodSegment != null && HasQuotedValues(moodSegment))
            {
                mood = ExtractQuotedValues(moodSegment).FirstOrDefault();
            }

            var moodScoreSegment = ExtractDirectiveSegment(line, "moodScore:");
            if (moodScoreSegment != null)
            {
                moodScore = ExtractIntValue(moodScoreSegment);
            }
            
            // Only strip directives from text if they were actually parsed
            var cleanedLine = line;

            if (tagsSegment != null && HasQuotedValues(tagsSegment))
                cleanedLine = RemoveDirectiveSegments(cleanedLine, "Tags:");

            if (moodSegment != null && HasQuotedValues(moodSegment))
                cleanedLine = RemoveDirectiveSegments(cleanedLine, "Mood:");

            if (moodScoreSegment != null && moodScore.HasValue)
            {
                cleanedLine = RemoveScalarDirective(cleanedLine, "MoodScore:");
            }
            
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
    
    private static int? ExtractIntValue(string input)
    {
        var score = int.Parse(input.Trim());
        
        if ( score is >= 1 and <= 5)
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

    private static string RemoveScalarDirective(string line, string directive)
    {
        return Regex.Replace(
            line,
            @$"{directive}\s*\d+",
            "",
            RegexOptions.IgnoreCase
        ).Trim();
    }


    private static string RemoveDirectiveSegments (string cleanedLine
                                                 , string directive)
    {
        var result = cleanedLine;

        result = Regex.Replace(result, @$"{directive}\s*(""[^""]+""\s*,?\s*)+", "", RegexOptions.IgnoreCase);
        //result = Regex.Replace(result, @"Mood:\s*""[^""]+""",            "", RegexOptions.IgnoreCase);

        return result.Trim();
    }

}
