namespace CognitivePlatform.Api.Integrations.Notifications;

public enum NotificationFeedback
{
    Tapped
  , Dismissed
  , ActedUpon
}

public interface INotificationPatternService
{
    Task<TimeOnly?> LearnedOpenDayTimeAsync  (CancellationToken ct = default);
    Task<TimeOnly?> LearnedCloseDayTimeAsync (CancellationToken ct = default);
    Task<TimeOnly?> LearnedJournalTimeAsync  (CancellationToken ct = default);
    Task RecordFeedbackAsync (string externalId, NotificationFeedback feedback, CancellationToken ct = default);
}
