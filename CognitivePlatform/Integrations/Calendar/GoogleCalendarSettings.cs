namespace CognitivePlatform.Api.Integrations.Calendar;

public class GoogleCalendarSettings
{
    public string ClientId               { get; set; } = string.Empty;
    public string ClientSecret           { get; set; } = string.Empty;
    public string RedirectUri            { get; set; } = "http://localhost:5273/auth/google/callback";
    public string TokenStorePartitionKey { get; set; } = "calendar-tokens";
}
