namespace CognitivePlatform.Api.Integrations.Notifications;

public interface INotificationScheduleProvider
{
    Task<NotificationSchedule> GetScheduleAsync(DateTimeOffset from, CancellationToken ct = default);
}
