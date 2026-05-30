using CognitivePlatform.Api.Integrations.Health.Models;

namespace CognitivePlatform.Api.Integrations.Health;

/// <summary>
/// Default IHealthProvider registered in DI before a phone endpoint is configured.
/// IsConnected always returns false; all data methods throw to prevent accidental use.
/// Replaced by HttpHealthProvider in Phase H.1-B.
/// </summary>
public sealed class DisconnectedHealthProvider : IHealthProvider
{
    public bool IsConnected => false;

    public Task<StepCountResult>  GetStepCountAsync (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => throw new InvalidOperationException("Health provider is not connected");

    public Task<SleepResult>      GetSleepAsync     (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => throw new InvalidOperationException("Health provider is not connected");

    public Task<HeartRateResult>  GetHeartRateAsync (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => throw new InvalidOperationException("Health provider is not connected");

    public Task<DistanceResult>   GetDistanceAsync  (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => throw new InvalidOperationException("Health provider is not connected");
}
