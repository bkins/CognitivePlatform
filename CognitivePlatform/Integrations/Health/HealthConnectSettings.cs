namespace CognitivePlatform.Api.Integrations.Health;

public sealed record HealthConnectSettings
{
    public string PhoneBaseUrl       { get; init; } = string.Empty;
    public string SharedSecret       { get; init; } = string.Empty;
    public int    PingTimeoutSeconds { get; init; } = 4;
}
