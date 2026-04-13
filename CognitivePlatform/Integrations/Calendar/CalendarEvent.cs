namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// A single calendar event returned by <see cref="ICalendarProvider"/>.
/// All times are in UTC. Callers format for display using local time as needed.
/// </summary>
public sealed class CalendarEvent
{
    public string         Id          { get; init; } = string.Empty;
    public string         Title       { get; init; } = string.Empty;
    public DateTimeOffset StartUtc    { get; init; }
    public DateTimeOffset EndUtc      { get; init; }

    /// <summary>True when the event occupies a full day and has no specific start/end time.</summary>
    public bool    IsAllDay     { get; init; }
    public string? Location     { get; init; }
    public string? Description  { get; init; }

    /// <summary>The display name of the calendar this event belongs to (e.g. "Work", "Ben's calendar").</summary>
    public string? CalendarName { get; init; }
}
