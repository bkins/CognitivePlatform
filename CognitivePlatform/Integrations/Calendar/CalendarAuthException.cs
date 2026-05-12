namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// Thrown by <see cref="ICalendarProvider"/> implementations when a calendar
/// operation cannot proceed because the OAuth access token is missing, expired,
/// or could not be refreshed.  Callers should surface a re-authorisation prompt
/// to the user rather than treating the result as an empty calendar.
/// </summary>
public class CalendarAuthException : Exception
{
    public CalendarAuthException()
        : base("Google Calendar authorization is invalid or expired. "
             + "Please re-connect at /auth/google/connect.") { }

    public CalendarAuthException(string message)
        : base(message) { }

    public CalendarAuthException(string message, Exception inner)
        : base(message, inner) { }
}
