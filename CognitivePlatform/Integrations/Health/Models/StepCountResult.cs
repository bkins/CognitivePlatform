namespace CognitivePlatform.Api.Integrations.Health.Models;

public sealed record StepCountResult
{
    public long    Steps           { get; init; }
    public double? DistanceMetres  { get; init; }
    public int?    ActiveCalories  { get; init; }
}
