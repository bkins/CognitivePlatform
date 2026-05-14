using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Integrations.Calendar;
using CP.Shared.Primitives.Avails.Extensions;

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
    [NaturalLanguageAction(Description = "Shows today's events from your Google Calendar."
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
        if (_calendar.IsConnected.Not()) return NotConnectedMessage();

        var localNow = DateTimeOffset.Now;
        var today   = localNow.Date;
        var offset  = localNow.Offset;
        var from    = new DateTimeOffset(today,            offset);
        var to      = new DateTimeOffset(today.AddDays(1), offset);

        IReadOnlyList<CalendarEvent> events;

        try
        {
            events = await _calendar.GetEventsAsync(from, to);
        }
        catch (CalendarAuthException)
        {
            return ReAuthMessage();
        }

        return events.Count == 0
                       ? $"No events on your calendar for {today:yyyy-MM-dd}."
                       : FormatEvents(events, $"Today's calendar ({today:yyyy-MM-dd})");
    }

    // -----------------------------------------------------------------------
    // GetEventsForDate
    // -----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Shows calendar events for a specific date. For week or multi-day queries, call this once per relevant day."
                         , Examples = new[]
                                      {
                                              "What's on my calendar tomorrow?"
                                            , "Show my schedule for Friday."
                                            , "Do I have anything on April 15?"
                                            , "What are my events for next Monday?"
                                            , "What's on my calendar this week?"
                                            , "Any meetings on Wednesday?"
                                      }
                         , Category = "calendar")]
    public async Task<string> GetEventsForDate( [NaturalLanguageParam(Description = "The date to look up, e.g. '2026-04-15' or a day name the LLM will convert to a date."
                                                                    , AllowEmpty  = false)]
                                                string date)
    {
        if (_calendar.IsConnected.Not()) return NotConnectedMessage();

        if (DateTimeOffset.TryParse(date, out var parsed)
                          .Not())
            return $"I couldn't parse '{date}' as a date. Please use a format like 'YYYY-MM-DD'.";

        var day    = parsed.LocalDateTime.Date;
        var offset = TimeZoneInfo.Local.GetUtcOffset(day);
        var from   = new DateTimeOffset(day,            offset);
        var to     = new DateTimeOffset(day.AddDays(1), offset);

        IReadOnlyList<CalendarEvent> events;

        try
        {
            events = await _calendar.GetEventsAsync(from, to);
        }
        catch (CalendarAuthException)
        {
            return ReAuthMessage();
        }

        return events.Count == 0
                       ? $"No events on your calendar for {day:yyyy-MM-dd}."
                       : FormatEvents(events, $"Calendar for {day:yyyy-MM-dd}");

    }

    // -----------------------------------------------------------------------
    // AddCalendarEvent
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Adds a new event to your Google Calendar."
                         , Examples = new[]
                                      {
                                              "Add a meeting called Sprint Review on Friday at 10am for 1 hour."
                                            , "Schedule a dentist appointment on April 20 at 2pm."
                                            , "Create a calendar event: Team lunch on Thursday at noon."
                                      }
                         , Category           = "calendar"
                         , AllowsClarification = true)]
    public async Task<string> AddCalendarEvent( [NaturalLanguageParam(Description = "Title or name of the event."
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
        if (_calendar.IsConnected.Not()) return NotConnectedMessage();

        if (DateTimeOffset.TryParse(startDateTime, out var start)
                          .Not())
            return $"I couldn't parse '{startDateTime}' as a start date/time. Please use ISO 8601 format, e.g. '2026-04-15T14:00:00'.";

        var end = DateTimeOffset.TryParse(endDateTime, out var parsedEnd)
                          ? parsedEnd
                          : start.AddHours(1);

        if (end <= start) return "The end time must be after the start time.";

        var created = await _calendar.AddEventAsync(title
                                                  , start
                                                  , end
                                                  , location.HasNoValue() 
                                                            ? null 
                                                            : location);

        if (created is null) return "Failed to create the calendar event. Please try again.";

        var time = $"{created.StartUtc.ToLocalTime():HH:mm} – {created.EndUtc.ToLocalTime():HH:mm}";
        var loc  = created.Location is not null ? $" at {created.Location}" : string.Empty;
        
        return $"Created: '{created.Title}' on {created.StartUtc.ToLocalTime():yyyy-MM-dd} {time}{loc}.";
    }

    // -----------------------------------------------------------------------
    // ListCalendars
    // -----------------------------------------------------------------------

    [FastPath]
    [NaturalLanguageAction(Description = "Lists all Google Calendars on the connected account with their current inclusion status."
                         , Examples = new[]
                                      {
                                              "Show my calendars."
                                            , "List my Google calendars."
                                            , "Which calendars am I subscribed to?"
                                            , "What calendars do I have?"
                                      }
                         , Category = "calendar")]
    public async Task<string> ListCalendars()
    {
        if (_calendar.IsConnected.Not()) return NotConnectedMessage();

        IReadOnlyList<CalendarSummary> summaries;

        try
        {
            summaries = await _calendar.GetCalendarListAsync();
        }
        catch (CalendarAuthException)
        {
            return ReAuthMessage();
        }

        var sb = new StringBuilder();
        sb.AppendLine("Your Google Calendars:");

        foreach (var summary in summaries)
        {
            var status = summary.IsSelected ? "included" : "excluded";
            sb.AppendLine($"  • {summary.Name} [{status}]");
        }

        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // SetCalendarInclusion
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Includes or excludes a Google Calendar from event queries and daily briefs."
                         , Examples = new[]
                                      {
                                              "Exclude my Family calendar from daily briefs."
                                            , "Include the Work calendar in my schedule."
                                            , "Stop showing events from Holidays in United States."
                                            , "Add the Birthdays calendar back to my schedule."
                                      }
                         , Category = "calendar")]
    public async Task<string> SetCalendarInclusion( [NaturalLanguageParam(Description = "The name or ID of the calendar to include or exclude."
                                                                        , AllowEmpty  = false)]
                                                    string calendarNameOrId
                                                  , [NaturalLanguageParam(Description = "True to include the calendar in event queries; false to exclude it."
                                                                        , AllowEmpty  = false)]
                                                    bool include )
    {
        if (_calendar.IsConnected.Not()) return NotConnectedMessage();

        IReadOnlyList<CalendarSummary> summaries;

        try
        {
            summaries = await _calendar.GetCalendarListAsync();
        }
        catch (CalendarAuthException)
        {
            return ReAuthMessage();
        }

        var match = summaries.FirstOrDefault(summary =>
                        summary.Name.Equals(calendarNameOrId, StringComparison.OrdinalIgnoreCase)
                     || summary.Id.Equals(calendarNameOrId,   StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return $"No calendar found with name or ID '{calendarNameOrId}'.";

        await _calendar.SetCalendarInclusionAsync(match.Id, include);

        var action = include ? "included" : "excluded";
        return $"Calendar '{match.Name}' is now {action} from your schedule.";
    }

    // -----------------------------------------------------------------------
    // FindFreeTime
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Finds free time slots on a given day that are long enough to fit a task of the specified duration."
                         , Examples = new[]
                                      {
                                              "Find me a free hour tomorrow."
                                            , "Do I have any free time on Friday for a 30-minute call?"
                                            , "When am I free on April 20 for at least 2 hours?"
                                      }
                         , Category = "calendar")]
    public async Task<string> FindFreeTime( [NaturalLanguageParam(Description = "The date to search for free time, e.g. '2026-04-15'."
                                                                , AllowEmpty = false)]
                                            string date
                                          , [NaturalLanguageParam(Description = "The required block length in minutes, e.g. 60 for one hour."
                                                                , AllowEmpty = false)]
                                            int durationMinutes )
    {
        if (_calendar.IsConnected.Not()) return NotConnectedMessage();

        if (DateTimeOffset.TryParse(date
                                  , out var parsed).Not())
            return $"I couldn't parse '{date}' as a date. Please use a format like 'YYYY-MM-DD'.";

        if (durationMinutes <= 0)
            return "Duration must be greater than zero minutes.";

        var day    = parsed.LocalDateTime.Date;
        var offset = TimeZoneInfo.Local.GetUtcOffset(day);
        var from   = new DateTimeOffset(day,            offset);
        var to     = new DateTimeOffset(day.AddDays(1), offset);

        IReadOnlyList<CalendarEvent> events;

        try
        {
            events = await _calendar.GetEventsAsync(from, to);
        }
        catch (CalendarAuthException)
        {
            return ReAuthMessage();
        }

        // Build a list of busy intervals from timed (non-all-day) events, sorted by start
        var busy = events.Where(calendarEvent => calendarEvent.IsAllDay.Not())
                         .Select(calendarEvent => (Start: calendarEvent.StartUtc
                                                    , End: calendarEvent.EndUtc))
                         .OrderBy(interval => interval.Start)
                         .ToList();

        // Working-hours window: 08:00–18:00 local time on the requested day
        var workStart = new DateTimeOffset(day.Year, day.Month, day.Day,  8, 0, 0, offset);
        var workEnd   = new DateTimeOffset(day.Year, day.Month, day.Day, 18, 0, 0, offset);

        var required  = TimeSpan.FromMinutes(durationMinutes);
        var freeSlots = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        var cursor = workStart;

        foreach (var (busyStart, busyEnd) in busy)
        {
            if (busyStart > cursor 
             && (busyStart - cursor) >= required)
                freeSlots.Add((cursor, busyStart));

            if (busyEnd > cursor)
                cursor = busyEnd;
        }

        // Check the gap from last event to end of working hours
        if (workEnd > cursor && (workEnd - cursor) >= required)
            freeSlots.Add((cursor, workEnd));

        if (freeSlots.Count == 0)
            return $"No free slots of {durationMinutes} minutes or longer found on {day:yyyy-MM-dd} within working hours (08:00–18:00).";

        var sb = new StringBuilder();
        sb.AppendLine($"Free slots on {day:yyyy-MM-dd} (>= {durationMinutes} min, within 08:00–18:00):");

        foreach (var (slotStart, slotEnd) in freeSlots)
        {
            var localStart = slotStart.ToLocalTime();
            var localEnd   = slotEnd.ToLocalTime();
            var slotLength = (int)(slotEnd - slotStart).TotalMinutes;

            sb.AppendLine($"• {localStart:HH:mm}–{localEnd:HH:mm}  ({slotLength} min available)");
        }

        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    public static string FormatEvents(IReadOnlyList<CalendarEvent> events, string heading)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading + ":");

        foreach (var calendarEvent in events)
        {
            if (calendarEvent.IsAllDay)
            {
                sb.Append($"• (all day) — {calendarEvent.Title}");
            }
            else
            {
                var start = calendarEvent.StartUtc.ToLocalTime();
                var end   = calendarEvent.EndUtc.ToLocalTime();
                
                sb.Append($"• {start:HH:mm}–{end:HH:mm} — {calendarEvent.Title}");
            }

            if (calendarEvent.Location is not null) sb.Append($" 📍 {calendarEvent.Location}");

            if (calendarEvent.CalendarName is not null)
                sb.Append($" [{calendarEvent.CalendarName}]");

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string NotConnectedMessage()
        => "Google Calendar is not connected. "
         + "Open http://localhost:<environmentPort>/auth/google/connect in your browser to authorise access, then try again.";

    private static string ReAuthMessage()
        => "Your Google Calendar session has expired. "
         + "Open http://localhost:<environmentPort>/auth/google/connect in your browser to re-authorise, then try again.";
}
