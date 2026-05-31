namespace CognitivePlatform.Api.Integrations.Notifications;

public sealed record NotificationFeedbackRequest
{
    public string ExternalId { get; init; } = string.Empty;
    public string Action     { get; init; } = string.Empty;
}
