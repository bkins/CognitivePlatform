namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// Deterministic calendar provider for local readiness and test runs where external
/// calendar calls must not occur.
/// </summary>
public sealed class DisconnectedCalendarProvider : ICalendarProvider
{
    public bool IsConnected => false;

    public string GetAuthorizationUrl() => string.Empty;

    public Task<bool> ExchangeCodeAsync(string code, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<CalendarSummary>> GetCalendarListAsync(CancellationToken ct = default)
        => throw new CalendarAuthException();

    public Task SetCalendarInclusionAsync( string            calendarId
                                         , bool              include
                                         , CancellationToken ct = default )
        => Task.CompletedTask;

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync( DateTimeOffset    fromUtc
                                                            , DateTimeOffset    toUtc
                                                            , CancellationToken ct = default )
        => throw new CalendarAuthException();

    public Task<CalendarEvent?> AddEventAsync( string           title
                                             , DateTimeOffset    startUtc
                                             , DateTimeOffset    endUtc
                                             , string?           location = null
                                             , CancellationToken ct       = default )
        => throw new CalendarAuthException();
}
