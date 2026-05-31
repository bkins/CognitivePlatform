namespace CognitivePlatform.Api.Integrations.Notifications;

public sealed class NotificationFeedbackRecord
{
    public string               Id         { get; set; } = string.Empty;
    public string               ExternalId { get; init; } = string.Empty;
    public NotificationFeedback Feedback   { get; init; }
    public DateTimeOffset       RecordedAt { get; init; }
}
