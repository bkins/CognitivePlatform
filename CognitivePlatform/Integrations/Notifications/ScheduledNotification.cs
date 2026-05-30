namespace CognitivePlatform.Api.Integrations.Notifications;

public sealed record ScheduledNotification
{
    public string               ExternalId { get; init; } = string.Empty;
    public string               Title      { get; init; } = string.Empty;
    public string               Body       { get; init; } = string.Empty;
    public DateTimeOffset       FireAt     { get; init; }
    public NotificationCategory Category   { get; init; }
}
