namespace CognitivePlatform.Api.Integrations.Notifications;

public sealed record NotificationSchedule
{
    public IReadOnlyList<ScheduledNotification> Notifications { get; init; } = [];
}
