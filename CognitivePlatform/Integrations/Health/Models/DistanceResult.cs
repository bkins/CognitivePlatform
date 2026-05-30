namespace CognitivePlatform.Api.Integrations.Health.Models;

public sealed record DistanceResult
{
    public double Metres          { get; init; }
    public long?  Steps           { get; init; }
    public int?   ActiveCalories  { get; init; }
}
