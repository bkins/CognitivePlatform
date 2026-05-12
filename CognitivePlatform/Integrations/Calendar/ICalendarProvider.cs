namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// Abstracts calendar access so the rest of the platform does not depend on Google APIs directly.
/// Only Google Calendar is implemented today (see DEFERRED.md for Outlook/Exchange).
/// </summary>
public interface ICalendarProvider
{
    /// <summary>
    /// True when credentials are configured AND OAuth tokens are stored.
    /// Consumers should check this before calling other members.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Returns the Google OAuth authorisation URL the user must visit in a browser
    /// to grant the application access to their calendar.
    /// </summary>
    string GetAuthorizationUrl();

    /// <summary>
    /// Exchanges the OAuth authorisation code (received at the callback URL) for
    /// access and refresh tokens, then persists them. Returns true on success.
    /// </summary>
    Task<bool> ExchangeCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Returns all calendars visible to the authenticated user, each annotated with
    /// its current inclusion state (see <see cref="SetCalendarInclusionAsync"/>).
    /// Throws <see cref="CalendarAuthException"/> when the token is invalid.
    /// </summary>
    Task<IReadOnlyList<CalendarSummary>> GetCalendarListAsync(CancellationToken ct = default);

    /// <summary>
    /// Includes or excludes a calendar from event queries.
    /// Pass <paramref name="include"/> = false to suppress events from that calendar.
    /// The change is persisted and survives process restarts.
    /// </summary>
    Task SetCalendarInclusionAsync( string           calendarId
                                  , bool             include
                                  , CancellationToken ct = default );

    /// <summary>
    /// Returns all events from the user's primary calendar in the given UTC window,
    /// ordered by start time. Returns an empty list when not connected or on error.
    /// Only calendars that pass the inclusion filter are queried.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync( DateTimeOffset    fromUtc
                                                     , DateTimeOffset    toUtc
                                                     , CancellationToken ct = default );

    /// <summary>
    /// Creates a new event on the user's primary calendar.
    /// Returns the created event on success, or null on error.
    /// </summary>
    Task<CalendarEvent?> AddEventAsync( string           title
                                      , DateTimeOffset    startUtc
                                      , DateTimeOffset    endUtc
                                      , string?           location = null
                                      , CancellationToken ct       = default );
}
