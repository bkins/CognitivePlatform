namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// Persisted calendar inclusion preferences.
/// Stored in IObjectStore with id "inclusion-list" under the token partition key.
/// An empty <see cref="ExcludedCalendarIds"/> list means all calendars are included.
/// </summary>
public class CalendarInclusionList
{
    public string         Id                  { get; set; } = "inclusion-list";
    public List<string>   ExcludedCalendarIds { get; set; } = new();
    public DateTimeOffset SavedUtc            { get; set; }
}
