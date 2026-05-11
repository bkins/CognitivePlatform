using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.Api.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// Google Calendar implementation of <see cref="ICalendarProvider"/>.
///
/// OAuth flow:
///   1. User visits GET /auth/google/connect  â†’  redirected to Google consent screen.
///   2. Google redirects to GET /auth/google/callback?code=...
///   3. This class exchanges the code for tokens and stores them in IObjectStore.
///   4. Subsequent calls use the stored access token, refreshing automatically on expiry.
///
/// All HTTP calls use the named "GoogleCalendar" HttpClient.
/// Token refresh uses the stored refresh token â€” if missing the user must re-authorise.
/// </summary>
public class GoogleCalendarProvider : ICalendarProvider
{
    private const string TokenUrl        = "https://oauth2.googleapis.com/token";
    private const string CalendarListUrl = "https://www.googleapis.com/calendar/v3/users/me/calendarList";
    private const string EventsBaseUrl   = "https://www.googleapis.com/calendar/v3/calendars";

    private readonly GoogleCalendarSettings  _settings;
    private readonly IObjectStore            _store;
    private readonly IHttpClientFactory      _http;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider( IOptions<GoogleCalendarSettings>      settings
                                 , IObjectStore                           store
                                 , IHttpClientFactory                     http
                                 , ILogger<GoogleCalendarProvider>        logger )
    {
        _settings = settings.Value;
        _store    = store;
        _http     = http;
        _logger   = logger;
    }

    // -----------------------------------------------------------------------
    // IsConnected
    // -----------------------------------------------------------------------

    public bool IsConnected
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId)
             || string.IsNullOrWhiteSpace(_settings.ClientSecret))
                return false;

            return _store.Get<CalendarTokens>("default", _settings.TokenStorePartitionKey) is not null;
        }
    }

    // -----------------------------------------------------------------------
    // GetAuthorizationUrl
    // -----------------------------------------------------------------------

    public string GetAuthorizationUrl()
    {
        var query = new Dictionary<string, string>
                    {
                            ["client_id"]     = _settings.ClientId
                          , ["redirect_uri"]  = _settings.RedirectUri
                          , ["response_type"] = "code"
                          , ["scope"]         = "https://www.googleapis.com/auth/calendar"
                          , ["access_type"]   = "offline"   // request refresh token
                          , ["prompt"]        = "consent"   // always show consent so refresh token is issued
                    };

        var queryString = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"https://accounts.google.com/o/oauth2/v2/auth?{queryString}";
    }

    // -----------------------------------------------------------------------
    // ExchangeCodeAsync
    // -----------------------------------------------------------------------

    public async Task<bool> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        using var client = _http.CreateClient("GoogleCalendar");

        var payload = new FormUrlEncodedContent(new Dictionary<string, string>
                                                {
                                                        ["code"]          = code
                                                      , ["client_id"]     = _settings.ClientId
                                                      , ["client_secret"] = _settings.ClientSecret
                                                      , ["redirect_uri"]  = _settings.RedirectUri
                                                      , ["grant_type"]    = "authorization_code"
                                                });

        var response = await client.PostAsync(TokenUrl, payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Token exchange failed ({Status}): {Body}", response.StatusCode, body);
            return false;
        }

        using var doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var       root = doc.RootElement;

        var tokens = new CalendarTokens
                     {
                             AccessToken  = root.GetProperty("access_token").GetString()!
                           , RefreshToken = root.TryGetProperty("refresh_token", out var rt)
                                                    ? rt.GetString()
                                                    : null
                           , ExpiresAt    = DateTimeOffset.UtcNow.AddSeconds(
                                 root.TryGetProperty("expires_in", out var exp)
                                         ? exp.GetInt32()
                                         : 3600)
                     };

        await _store.Save(tokens, _settings.TokenStorePartitionKey, "default");
        _logger.LogInformation("Google Calendar tokens stored successfully");
        return true;
    }

    // -----------------------------------------------------------------------
    // GetEventsAsync
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync( DateTimeOffset    fromUtc
                                                                   , DateTimeOffset    toUtc
                                                                   , CancellationToken ct = default )
    {
        var accessToken = await GetValidAccessTokenAsync(ct);
        if (accessToken is null)
        {
            _logger.LogWarning("No valid access token â€” returning empty event list");
            return [];
        }

        var calendars = await GetCalendarListAsync(accessToken, ct);

        // Fan out in parallel â€” one request per calendar
        var fetchTasks = calendars
            .Select(cal => FetchEventsForCalendarAsync(cal.Id, cal.Name, accessToken, fromUtc, toUtc, ct));

        var results = await Task.WhenAll(fetchTasks);

        return results.SelectMany(events => events)
                      .OrderBy(evt => evt.StartUtc)
                      .ToList();
    }

    private record CalendarInfo(string Id, string Name);

    /// <summary>
    /// Returns all calendars visible to the authenticated user (ID + display name).
    /// Falls back to a single primary calendar entry if the list cannot be fetched.
    /// </summary>
    private async Task<IReadOnlyList<CalendarInfo>> GetCalendarListAsync( string            accessToken
                                                                         , CancellationToken ct )
    {
        using var client  = _http.CreateClient("GoogleCalendar");
        using var request = new HttpRequestMessage(HttpMethod.Get, CalendarListUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch calendar list ({Status}) â€” falling back to primary", response.StatusCode);
            return [new CalendarInfo("primary", "Primary")];
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        if (!doc.RootElement.TryGetProperty("items", out var items))
            return [new CalendarInfo("primary", "Primary")];

        var calendars = items.EnumerateArray()
                             .Select(cal =>
                             {
                                 var id   = cal.TryGetProperty("id",      out var idProp)   ? idProp.GetString()   : null;
                                 var name = cal.TryGetProperty("summary", out var nameProp) ? nameProp.GetString() : null;
                                 return id is not null ? new CalendarInfo(id, name ?? id) : null;
                             })
                             .Where(cal => cal is not null)
                             .Select(cal => cal!)
                             .ToList();

        return calendars.Count > 0 ? calendars : [new CalendarInfo("primary", "Primary")];
    }

    /// <summary>
    /// Fetches events from a single calendar. Returns an empty list on any error
    /// so a problem with one shared calendar cannot suppress the others.
    /// </summary>
    private async Task<IReadOnlyList<CalendarEvent>> FetchEventsForCalendarAsync( string            calendarId
                                                                                 , string            calendarName
                                                                                 , string            accessToken
                                                                                 , DateTimeOffset    fromUtc
                                                                                 , DateTimeOffset    toUtc
                                                                                 , CancellationToken ct )
    {
        var encodedId = Uri.EscapeDataString(calendarId);
        var url       = $"{EventsBaseUrl}/{encodedId}/events"
                      + $"?timeMin={Uri.EscapeDataString(fromUtc.ToString("O"))}"
                      + $"&timeMax={Uri.EscapeDataString(toUtc.ToString("O"))}"
                      + "&singleEvents=true&orderBy=startTime";

        using var client  = _http.CreateClient("GoogleCalendar");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch events for calendar '{CalendarId}' ({Status})", calendarId, response.StatusCode);
            return [];
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseEventList(doc.RootElement, calendarName);
    }

    // -----------------------------------------------------------------------
    // AddEventAsync
    // -----------------------------------------------------------------------

    public async Task<CalendarEvent?> AddEventAsync( string           title
                                                    , DateTimeOffset    startUtc
                                                    , DateTimeOffset    endUtc
                                                    , string?           location = null
                                                    , CancellationToken ct       = default )
    {
        var accessToken = await GetValidAccessTokenAsync(ct);
        if (accessToken is null) return null;

        var body = new
                   {
                           summary  = title
                         , location
                         , start    = new { dateTime = startUtc.ToString("O"), timeZone = "UTC" }
                         , end      = new { dateTime = endUtc.ToString("O"),   timeZone = "UTC" }
                   };

        using var client  = _http.CreateClient("GoogleCalendar");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{EventsBaseUrl}/primary/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(body);

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Failed to create event ({Status}): {Body}", response.StatusCode, errorBody);
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseSingleEvent(doc.RootElement, "Primary");
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<string?> GetValidAccessTokenAsync(CancellationToken ct)
    {
        var tokens = _store.Get<CalendarTokens>("default", _settings.TokenStorePartitionKey);
        if (tokens is null) return null;

        // Token still valid with a 5-minute safety buffer
        if (tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return tokens.AccessToken;

        // Access token expired â€” use refresh token to get a new one
        if (tokens.RefreshToken is null)
        {
            _logger.LogWarning("Access token expired and no refresh token stored; re-authorisation required");
            return null;
        }

        return await RefreshAccessTokenAsync(tokens, ct);
    }

    private async Task<string?> RefreshAccessTokenAsync(CalendarTokens tokens, CancellationToken ct)
    {
        using var client = _http.CreateClient("GoogleCalendar");

        var payload = new FormUrlEncodedContent(new Dictionary<string, string>
                                                {
                                                        ["client_id"]     = _settings.ClientId
                                                      , ["client_secret"] = _settings.ClientSecret
                                                      , ["refresh_token"] = tokens.RefreshToken!
                                                      , ["grant_type"]    = "refresh_token"
                                                });

        var response = await client.PostAsync(TokenUrl, payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Token refresh failed ({Status})", response.StatusCode);
            return null;
        }

        using var doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var       root = doc.RootElement;

        tokens.AccessToken = root.GetProperty("access_token").GetString()!;
        tokens.ExpiresAt   = DateTimeOffset.UtcNow.AddSeconds(
            root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600);

        // Refresh token is not re-issued on every refresh â€” keep the existing one
        await _store.Save(tokens, _settings.TokenStorePartitionKey, "default");

        _logger.LogInformation("Google Calendar access token refreshed successfully");
        return tokens.AccessToken;
    }

    // -----------------------------------------------------------------------
    // JSON parsing
    // -----------------------------------------------------------------------

    private static IReadOnlyList<CalendarEvent> ParseEventList(JsonElement root, string calendarName)
    {
        if (!root.TryGetProperty("items", out var items))
            return [];

        return items.EnumerateArray()
                    .Select(item => ParseSingleEvent(item, calendarName))
                    .Where(evt => evt is not null)
                    .Select(evt => evt!)
                    .ToList();
    }

    private static CalendarEvent? ParseSingleEvent(JsonElement item, string calendarName)
    {
        try
        {
            var id       = item.TryGetProperty("id",       out var idProp)      ? idProp.GetString()      ?? string.Empty : string.Empty;
            var title    = item.TryGetProperty("summary",  out var summaryProp) ? summaryProp.GetString() ?? "(No title)" : "(No title)";
            var location = item.TryGetProperty("location", out var locProp)     ? locProp.GetString()     : null;

            var start = item.GetProperty("start");
            var end   = item.GetProperty("end");

            // All-day events use "date"; timed events use "dateTime"
            bool           isAllDay;
            DateTimeOffset startUtc, endUtc;

            if (start.TryGetProperty("date", out var startDate))
            {
                isAllDay = true;
                startUtc = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(startDate.GetString()!), DateTimeKind.Local));
                endUtc   = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(end.GetProperty("date").GetString()!), DateTimeKind.Local));
            }
            else
            {
                isAllDay = false;
                startUtc = DateTimeOffset.Parse(start.GetProperty("dateTime").GetString()!);
                endUtc   = DateTimeOffset.Parse(end.GetProperty("dateTime").GetString()!);
            }

            return new CalendarEvent
                   {
                           Id           = id
                         , Title        = title
                         , StartUtc     = startUtc
                         , EndUtc       = endUtc
                         , IsAllDay     = isAllDay
                         , Location     = location
                         , CalendarName = calendarName
                   };
        }
        catch
        {
            // Malformed event â€” skip it rather than crashing the whole list
            return null;
        }
    }
}
