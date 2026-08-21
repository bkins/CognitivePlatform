namespace CognitivePlatform.Api.Startup;

/// <summary>Crash report payload posted by the LAA Android client.</summary>
public sealed record ClientCrashReport
{
    public string   Platform   { get; init; } = "Android";
    public string   Message    { get; init; } = string.Empty;
    public string   StackTrace { get; init; } = string.Empty;
    public string   Source     { get; init; } = string.Empty;
    public DateTime Timestamp  { get; init; } = DateTime.UtcNow;
}
