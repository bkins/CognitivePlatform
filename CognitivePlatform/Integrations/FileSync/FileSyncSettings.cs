namespace CognitivePlatform.Api.Integrations.FileSync;

public sealed record FileSyncSettings
{
    public string GatewayBaseUrl     { get; init; } = string.Empty;
    public string SharedSecret       { get; init; } = string.Empty;
    public string DeviceName         { get; init; } = "Phone";
    public int    PingTimeoutSeconds { get; init; } = 4;
}
