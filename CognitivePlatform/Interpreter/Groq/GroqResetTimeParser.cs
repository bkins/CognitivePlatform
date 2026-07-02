using System.Text.RegularExpressions;
using CP.Shared.Primitives.Avails;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Parses Groq's rate-limit reset duration strings into a <see cref="TimeSpan"/>.
/// Groq returns values like "1m30s", "2h", "45s", "1h30m" — not ISO 8601.
/// </summary>
public static class GroqResetTimeParser
{
    // Matches optional hours, minutes, seconds — all components are optional
    // but at least one must be present.  e.g. "1h", "30s", "1m30s", "1h30m20s"
    private static readonly Regex DurationPattern = new(RegexMatchingPatterns.DurationParserPattern
                                                      , RegexOptions.Compiled 
                                                      | RegexOptions.ExplicitCapture);

    /// <summary>
    /// Parses a Groq reset string into a <see cref="TimeSpan"/>.
    /// Returns <see cref="TimeSpan.Zero"/> if the string is empty or unrecognised.
    /// </summary>
    public static TimeSpan Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TimeSpan.Zero;

        var match = DurationPattern.Match(raw.Trim());

        if (match.Success.Not())
            return TimeSpan.Zero;

        var hours   = ParseGroup(match, "hours");
        var minutes = ParseGroup(match, "minutes");
        var seconds = ParseGroup(match, "seconds");

        if (hours == 0 && minutes == 0 && seconds == 0)
            return TimeSpan.Zero;

        return new TimeSpan(hours, minutes, seconds);
    }

    /// <summary>
    /// Converts a Groq reset string to the approximate local wall-clock time
    /// at which the window will reset, formatted as "h:mm tt" (e.g. "7:13 PM").
    /// Returns null if the string cannot be parsed.
    /// </summary>
    public static string? ToApproximateLocalTime(string? raw)
    {
        var duration = Parse(raw);

        if (duration == TimeSpan.Zero)
            return null;

        var resetAt = DateTimeOffset.Now.Add(duration);

        return resetAt.ToString("h:mm tt");
    }

    private static int ParseGroup(Match match, string groupName)
    {
        var group = match.Groups[groupName];

        return group.Success && int.TryParse(group.Value, out var value)
                       ? value
                       : 0;
    }
}