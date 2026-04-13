namespace CognitivePlatform.Api.Integrations.Calendar;

/// <summary>
/// Persisted OAuth tokens for Google Calendar.
/// Stored in IObjectStore with id "default" under the configured partition key.
/// </summary>
public class CalendarTokens
{
    /// <summary>Fixed id used as the object store key — only one token set is stored.</summary>
    public string         Id           { get; set; } = "default";
    public string         AccessToken  { get; set; } = string.Empty;

    /// <summary>Null when the authorisation server did not issue a refresh token.</summary>
    public string?        RefreshToken { get; set; }
    public DateTimeOffset ExpiresAt    { get; set; }
}
