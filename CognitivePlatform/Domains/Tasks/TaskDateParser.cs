using System.Text.RegularExpressions;
using CP.Shared.Primitives.Avails;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// Static helper for parsing natural-language date expressions and extracting
/// due-date suffixes from free-form task title strings.
/// </summary>
/// <remarks>
/// Extracted from <see cref="TaskActions"/> so that <see cref="DailyRecordService"/>
/// can honour due-date hints embedded in titles that arrive via the Plan/Check commands.
/// </remarks>
public static class TaskDateParser
{
    // Matches trailing "by ...", "due ...", "due by ...", "due on ...", "until ..." clauses.
    // RightToLeft so the LAST occurrence wins (e.g. "Move meeting due on May 7 by EOD").
    private static readonly Regex DueDateSuffixPattern = new(RegexMatchingPatterns.DueDateSuffixPattern
                                                           , RegexOptions.IgnoreCase 
                                                           | RegexOptions.Compiled 
                                                           | RegexOptions.RightToLeft);

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Attempts to extract a due-date phrase from the tail of <paramref name="title"/>.
    /// On success <paramref name="cleanTitle"/> has the suffix stripped and
    /// <paramref name="dueDate"/> is the parsed <see cref="DateTimeOffset"/>.
    /// On failure <paramref name="cleanTitle"/> equals the original title and
    /// <paramref name="dueDate"/> is <c>null</c>.
    /// </summary>
    public static bool TryExtractDueDateFromTitle( string              title
                                                 , out string          cleanTitle
                                                 , out DateTimeOffset? dueDate )
    {
        cleanTitle = title;
        dueDate    = null;

        // Try colon format first: e.g. "due:today", "due: tomorrow"
        var colonMatch = Regex.Match(title, @"\s+(?:due|by|until)\s*:\s*([\w\s/,.\-]+?)[\s,;.]*$", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        if (colonMatch.Success)
        {
            var dateText = colonMatch.Groups[1].Value.Trim();
            if (TryParseDate(dateText, out var parsed))
            {
                cleanTitle = title[..colonMatch.Index].Trim();
                dueDate    = parsed;
                return true;
            }
        }

        var match = DueDateSuffixPattern.Match(title);

        if (!match.Success) return false;

        var dateText2 = match.Groups[1].Value.Trim();

        if (!TryParseDate(dateText2, out var parsed2)) return false;

        cleanTitle = title[..match.Index].Trim();
        dueDate    = parsed2;

        return true;
    }

    /// <summary>
    /// Parses a natural-language or ISO date expression into a
    /// <see cref="DateTimeOffset"/> anchored to UTC now.
    /// Supported expressions include: today, tomorrow, yesterday, next week,
    /// end of week / weekend, end of month, weekday names ("friday", "next monday"),
    /// relative offsets ("in 3 days", "in 2 weeks", "in 1 month"), and any format
    /// accepted by <see cref="DateTimeOffset.TryParse(string, out DateTimeOffset)"/>.
    /// </summary>
    public static bool TryParseDate( string?           text
                                   , out DateTimeOffset value )
    {
        value = default;

        if (!text.HasValue()) return false;

        // Anchor relative dates to the server's local clock so that "today" means
        // the user's calendar day. The stored DateTimeOffset preserves the local
        // offset, so UTC comparisons elsewhere still work correctly.
        // Single-user/same-machine assumption — when multi-tz becomes real, source
        // the offset from ConversationContext metadata instead.
        var now        = DateTimeOffset.Now;
        var normalized = text!.Trim().ToLowerInvariant();

        // ── Simple keyword tokens ────────────────────────────────────────────
        switch (normalized)
        {
            case "today":
                value = now.Date;
                return true;

            case "tomorrow":
                value = now.Date.AddDays(1);
                return true;

            case "yesterday":
                value = now.Date.AddDays(-1);
                return true;

            case "next week":
                value = now.Date.AddDays(7);
                return true;

            case "end of week" or "this weekend" or "weekend":
                value = NextDayOfWeek(now.Date, DayOfWeek.Saturday);
                return true;

            case "end of month":
                value = new DateTimeOffset( now.Year
                                          , now.Month
                                          , DateTime.DaysInMonth(now.Year, now.Month)
                                          , 0, 0, 0
                                          , now.Offset);
                return true;
        }

        // ── "next <weekday>" or plain "<weekday>" ────────────────────────────
        var isNext   = normalized.StartsWith("next ");
        var dayToken = isNext ? normalized["next ".Length..] : normalized;

        if (TryParseDayOfWeekToken(dayToken, out var dow))
        {
            // "next monday" always jumps to the coming Monday even if today IS Monday.
            // Plain "monday" snaps to the next occurrence (today counts if it is today).
            value = isNext
                        ? NextDayOfWeek(now.Date.AddDays(1), dow)
                        : NextDayOfWeek(now.Date, dow);
            return true;
        }

        // ── "in N days / weeks / months" ────────────────────────────────────
        var inMatch = Regex.Match(normalized, RegexMatchingPatterns.RelativeTimeframePattern);

        if (inMatch.Success)
        {
            var count = int.Parse(inMatch.Groups[1].Value);
            var unit  = inMatch.Groups[2].Value;

            value = unit.StartsWith("month")
                        ? now.Date.AddMonths(count)
                        : unit.StartsWith("week")
                            ? now.Date.AddDays(count * 7)
                            : now.Date.AddDays(count);
            return true;
        }

        // ── Absolute date strings ("April 5th", "2026-04-05", etc.) ─────────
        if (DateTimeOffset.TryParse(text, out value))
            return true;

        return false;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Returns the next occurrence of <paramref name="target"/> on or after <paramref name="from"/>.</summary>
    public static DateTimeOffset NextDayOfWeek( DateTimeOffset from
                                              , DayOfWeek     target )
    {
        var daysUntil = ((int)target - (int)from.DayOfWeek + 7) % 7;
        return from.Date.AddDays(daysUntil == 0 ? 7 : daysUntil);
    }

    public static bool TryParseDayOfWeekToken( string token, out DayOfWeek result )
    {
        switch (token)
        {
            case "monday"    or "mon": result = DayOfWeek.Monday;    return true;
            case "tuesday"   or "tue": result = DayOfWeek.Tuesday;   return true;
            case "wednesday" or "wed": result = DayOfWeek.Wednesday; return true;
            case "thursday"  or "thu": result = DayOfWeek.Thursday;  return true;
            case "friday"    or "fri": result = DayOfWeek.Friday;    return true;
            case "saturday"  or "sat": result = DayOfWeek.Saturday;  return true;
            case "sunday"    or "sun": result = DayOfWeek.Sunday;    return true;
            default:
                result = default;
                return false;
        }
    }
}
