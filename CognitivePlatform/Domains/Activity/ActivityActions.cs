using System.ComponentModel;
using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Activity;

/// <summary>
/// Natural-language surface for explicit activity/habit logging.
///
/// Design decisions:
/// - <c>type</c> is free-text (lowercase-normalised, trimmed). A closed enum would churn
///   while the vocabulary is still user-driven.
/// - <c>tags</c> arrives as a comma-separated string from the NL layer and is split into
///   a proper <see cref="List{String}"/> before being stored ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â consistent with
///   <c>JournalActions.AddJournalEntry</c>.
/// - <c>ListActivities</c> defaults to the last 7 days when no range is supplied, to keep
///   prompt context and summaries tight. Explicit dates override the default.
/// </summary>
[Category("activity")]
[Domain(typeof(SystemDomain))]
public sealed class ActivityActions
{
    private readonly IActivityLog _activityLog;

    public ActivityActions(IActivityLog activityLog)
    {
        _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
    }

    // ----------------------------------------------------------------------
    // LogActivity
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(
            Description = "Records an explicit activity or habit event ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â e.g. running, meditation, reading ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â with optional duration, unit, notes, and tags."
          , Examples = new[]
                       {
                               "Log that I ran for 30 minutes."
                             , "Record 10 minutes of meditation."
                             , "I read 40 pages today."
                             , "Log a walk, 20 minutes, tagged outdoors."
                             , "Record strength training, 45 minutes, notes felt strong."
                       }
          , Category = "activity", IsReplayable = true)]
    public async Task<string> LogActivity (
            [NaturalLanguageParam(Description = "The activity type ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â e.g. 'run', 'meditation', 'reading'."
                                , AllowEmpty  = false)]
            string type
          , [NaturalLanguageParam(Description  = "Optional quantity ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â e.g. 30 (minutes), 40 (pages), 20 (reps)."
                                , Optional     = true)]
            int? duration = null
          , [NaturalLanguageParam(Description  = "Optional unit for the quantity ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â e.g. 'minutes', 'pages', 'reps'."
                                , Optional     = true)]
            string? unit = null
          , [NaturalLanguageParam(Description  = "Optional free-text notes about the activity."
                                , Optional     = true)]
            string? notes = null
          , [NaturalLanguageParam(Description  = "Optional tags (comma-separated) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â e.g. 'outdoors, cardio'."
                                , Optional     = true
                                , DefaultValue = "")]
            string? tags = null)
    {
        var normalisedType = (type ?? string.Empty).Trim().ToLowerInvariant();

        if (normalisedType.Length == 0)
            return "Cannot log activity: type is required.";

        var activityEvent = new ActivityEvent
                            {
                                    OccurredUtc  = DateTimeOffset.UtcNow
                                  , ActivityType = normalisedType
                                  , Duration     = duration
                                  , Unit         = NormaliseOptional(unit)
                                  , Notes        = NormaliseOptional(notes)
                                  , Tags         = SplitCommaSeparated(tags)
                            };

        await _activityLog.LogAsync(activityEvent).ConfigureAwait(false);

        return BuildConfirmation(activityEvent);
    }

    // ----------------------------------------------------------------------
    // ListActivities
    // ----------------------------------------------------------------------
    [FastPath]
    [NaturalLanguageAction(
            Description = "Lists activity/habit events, grouped by type, optionally filtered by date. Defaults to the last 7 days."
          , Examples = new[]
                       {
                               "Show my activities this week."
                             , "List what I've done recently."
                             , "What activities did I log last month?"
                             , "Show activities from 2026-03-01 to 2026-03-31."
                       }
          , Category = "activity")]
    public async Task<string> ListActivities (
            [NaturalLanguageParam(Description  = "Start date (optional)."
                                , Optional     = true
                                , DefaultValue = "")]
            DateTime? fromDate = null
          , [NaturalLanguageParam(Description  = "End date (optional)."
                                , Optional     = true
                                , DefaultValue = "")]
            DateTime? toDate = null)
    {
        var fromUtc = fromDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc))
                                        : (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-7);
        var toUtc   = toDate.HasValue   ? new DateTimeOffset(DateTime.SpecifyKind(toDate.Value,   DateTimeKind.Utc))
                                        : (DateTimeOffset?)null;

        var events = await _activityLog.ListAsync(fromUtc, toUtc).ConfigureAwait(false);

        if (events.Count == 0)
            return "No activities logged for the specified date range.";

        var grouped = events.GroupBy(activityEvent => activityEvent.ActivityType)
                            .OrderByDescending(group => group.Count())
                            .ThenBy           (group => group.Key);

        var sb = new StringBuilder();
        sb.AppendLine($"Found {events.Count} {(events.Count == 1 ? "activity" : "activities")}:");
        sb.AppendLine();

        foreach (var group in grouped)
        {
            sb.AppendLine($"=== {ToHumanActivityType(group.Key)} ({group.Count()}) ===");

            foreach (var activityEvent in group.OrderByDescending(item => item.OccurredUtc))
            {
                sb.Append($"[{activityEvent.OccurredUtc.ToLocalTime():yyyy-MM-dd h:mm tt}]");

                if (activityEvent.Duration.HasValue)
                {
                    sb.Append(' ');
                    sb.Append(activityEvent.Duration.Value);
                    if (!string.IsNullOrWhiteSpace(activityEvent.Unit))
                        sb.Append($" {activityEvent.Unit}");
                }

                if (!string.IsNullOrWhiteSpace(activityEvent.Notes))
                    sb.Append($" ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â {RedactGuids(activityEvent.Notes)}");

                if (activityEvent.Tags.Count > 0)
                    sb.Append($" [tags: {string.Join(", ", activityEvent.Tags)}]");

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------
    private static string BuildConfirmation(ActivityEvent activityEvent)
    {
        var sb = new StringBuilder();
        sb.Append("Logged ").Append(activityEvent.ActivityType);

        if (activityEvent.Duration.HasValue)
        {
            sb.Append(" ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â ").Append(activityEvent.Duration.Value);
            if (!string.IsNullOrWhiteSpace(activityEvent.Unit))
                sb.Append(' ').Append(activityEvent.Unit);
        }

        if (activityEvent.Tags.Count > 0)
            sb.Append(" [tags: ").Append(string.Join(", ", activityEvent.Tags)).Append(']');

        sb.Append('.');
        return sb.ToString();
    }

    private static string ToHumanActivityType(string activityType)
    {
        return activityType switch
        {
                "insight.emitted"         => "Insight surfaced"
              , "insight.provider_failed" => "Provider issue"
              , "insight.weave_failed"    => "Weave failed"
              , "insight.deduplicated"    => "Insight skipped (duplicate)"
              , _                         => activityType
        };
    }

    /// <summary>
    /// Strips 32-character lowercase hex sequences (object-store GUIDs) from a string
    /// so they never surface in user-facing output.
    /// </summary>
    private static string RedactGuids(string notes)
    {
        return global::System.Text.RegularExpressions.Regex.Replace(
                   notes
                 , @"[0-9a-f]{32}"
                 , string.Empty
                 , global::System.Text.RegularExpressions.RegexOptions.IgnoreCase)
               .TrimEnd('.', '-', ' ');
    }

    private static string? NormaliseOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim();
    }

    private static List<string> SplitCommaSeparated(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where (item => item.Length > 0)
                    .ToList();
    }
}
