namespace CognitivePlatform.Api.Integrations.Health.Models;

public sealed record HeartRateResult
{
    public int  AverageBpm   { get; init; }
    public int? MinBpm       { get; init; }
    public int? MaxBpm       { get; init; }
    public int? RestingBpm   { get; init; }
}
