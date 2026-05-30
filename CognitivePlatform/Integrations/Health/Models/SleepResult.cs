namespace CognitivePlatform.Api.Integrations.Health.Models;

public sealed record SleepResult
{
    public int  TotalMinutes  { get; init; }
    public int? DeepMinutes   { get; init; }
    public int? RemMinutes    { get; init; }
    public int? LightMinutes  { get; init; }
    public int  Sessions      { get; init; }
}
