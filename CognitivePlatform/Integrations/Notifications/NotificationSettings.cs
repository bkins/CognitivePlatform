namespace CognitivePlatform.Api.Integrations.Notifications;

public sealed class NotificationSettings
{
    public int MaxPerDay       { get; init; } = 5;
    public int MinGapMinutes   { get; init; } = 90;
    public int QuietHoursStart { get; init; } = 22;
    public int QuietHoursEnd   { get; init; } = 7;
}
