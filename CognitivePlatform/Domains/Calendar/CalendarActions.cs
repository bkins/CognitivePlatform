using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Integrations.Calendar;

namespace CognitivePlatform.Api.Domains.Calendar;

/// <summary>
/// Natural language actions that surface Google Calendar data in the conversation.
/// All actions gracefully return a "not connected" message when the OAuth flow
/// has not been completed — the user is guided to /auth/google/connect.
/// </summary>
public class CalendarActions
{
    private readonly ICalendarProvider _calendar;

    public CalendarActions(ICalendarProvider calendar)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    // -----------------------------------------------------------------------
    // GetTodayEvents
    // -----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(
            Description = "Shows today's events from your Google Calendar."
          , Examples = new[]
                       {
                               "What's on my calendar today?"
                             , "Show my calendar for today."
                             , "Any meetings today?"
                             , "What do I have scheduled today?"
                       }
          , Category = "calendar")]
    public async Task<string> GetTodayEvents()
    {
        if (!_calendar.IsConnected)
            return NotConnectedMessage();

        var today = DateTimeOffset.UtcNow.Date;
        var from  = new DateTimeOffset(today,          TimeSpan.Zero);
        var to    = new DateTimeOffset(today.AddDays(1), TimeSpan.Zero);

        var events = await _calendar.GetEventsAsync(from, to);

        if (events.Count == 0)
            return $"No events on your calendar for {today:yyyy-MM-dd}.";

        return FormatEvents(events, $"Today's calendar ({today:yyyy-MM-dd})");
    }

    // -----------------------------------------------------------------------
    // GetEventsForDate
    // -----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(
            Description = "Shows calendar events for a specific date."
          , Examples = new[]
                       {
                               "What's on my calendar tomorrow?"
                             , "Show my schedule for Friday."
                             , "Do I have anything on April 15?"
                             , "What are my events for next Monday?"
                       }
          , Category = "calendar")]
    public async Task<string> GetEventsForDate(
            [NaturalLanguageParam(Description = "The date to look up, e.g. '2026-04-15' or a day name the LLM will convert to a date."
                                , AllowEmpty  = false)]
            string date)
    {
        if (!_calendar.IsConnected)
            return NotConnectedMessage();

        if (!DateTimeOffset.TryParse(date, out var parsed))
            return $"I couldn't parse '{date}' as a date. Please use a format like 'YYYY-MM-DD'.";

        var day  = parsed.UtcDateTime.Date;
        var from = new DateTimeOffset(day,          TimeSpan.Zero);
        var to   = new DateTimeOffset(day.AddDays(1), TimeSpan.Zero);

        var events = await _calendar.GetEventsAsync(from, to);

        if (events.Count == 0)
            return $"No events on your calendar for {day:yyyy-MM-dd}.";

        return FormatEvents(events, $"Calendar for {day:yyyy-MM-dd}");
    }

    // -----------------------------------------------------------------------
    // AddCalendarEvent
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
            Description = "Adds a new event to your Google Calendar."
          , Examples = new[]
                       {
                               "Add a meeting called Sprint Review on Friday at 10am for 1 hour."
                             , "Schedule a dentist appointment on April 20 at 2pm."
                             , "Create a calendar event: Team lunch on Thursday at noon."
                       }
          , Category           = "calendar"
          , AllowsClarification = true)]
    public async Task<string> AddCalendarEvent(
            [NaturalLanguageParam(Description = "Title or name of the event."
                                , AllowEmpty  = false)]
            string title
          , [NaturalLanguageParam(Description = "Start date and time in ISO 8601 format, e.g. '2026-04-15T14:00:00'."
                                , AllowEmpty  = false)]
            string startDateTime
          , [NaturalLanguageParam(Description  = "End date and time in ISO 8601 format. Defaults to 1 hour after the start time."
                                , Optional     = true
                                , DefaultValue = "")]
            string? endDateTime = null
          , [NaturalLanguageParam(Description  = "Optional location for the event."
                                , Optional     = true
                                , DefaultValue = "")]
            string? location = null)
    {
        if (!_calendar.IsConnected)
            return NotConnectedMessage();

        if (!DateTimeOffset.TryParse(startDateTime, out var start))
            return $"I couldn't parse '{startDateTime}' as a start date/time. Please use ISO 8601 format, e.g. '2026-04-15T14:00:00'.";

        var end = DateTimeOffset.TryParse(endDateTime, out var parsedEnd)
                          ? parsedEnd
                          : start.AddHours(1);

        if (end <= start)
            return "The end time must be after the start time.";

        var created = await _calendar.AddEventAsync(title, start, end, string.IsNullOrWhiteSpace(location) ? null : location);

        if (created is null)
            return "Failed to create the calendar event. Please try again.";

        var time = $"{created.StartUtc.ToLocalTime():HH:mm} – {created.EndUtc.ToLocalTime():HH:mm}";
        var loc  = created.Location is not null ? $" at {created.Location}" : string.Empty;
        return $"Created: '{created.Title}' on {created.StartUtc.ToLocalTime():yyyy-MM-dd} {time}{loc}.";
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FormatEvents(IReadOnlyList<CalendarEvent> events, string heading)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading + ":");

        foreach (var evt in events)
        {
            if (evt.IsAllDay)
            {
                sb.Append($"• (all day) — {evt.Title}");
            }
            else
            {
                var start = evt.StartUtc.ToLocalTime();
                var end   = evt.EndUtc.ToLocalTime();
                sb.Append($"• {start:HH:mm}–{end:HH:mm} — {evt.Title}");
            }

            if (evt.Location is not null)
                sb.Append($" 📍 {evt.Location}");

            if (evt.CalendarName is not null)
                sb.Append($" [{evt.CalendarName}]");

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string NotConnectedMessage()
        => "Google Calendar is not connected. "
         + "Open http://localhost:5273/auth/google/connect in your browser to authorise access, then try again.";
}
