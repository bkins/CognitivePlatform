using CognitivePlatform.Api.Integrations.Health.Models;

namespace CognitivePlatform.Api.Integrations.Health;

/// <summary>
/// Abstracts health data access so the rest of the platform does not depend on
/// Android Health Connect directly. <see cref="DisconnectedHealthProvider"/> is the DI
/// default until a phone is configured (Phase H.1-B).
/// </summary>
public interface IHealthProvider
{
    /// <summary>
    /// True when a phone endpoint is reachable and responding.
    /// Consumers should check this before calling other members.
    /// </summary>
    bool IsConnected { get; }

    Task<StepCountResult>  GetStepCountAsync (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<SleepResult>      GetSleepAsync     (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<HeartRateResult>  GetHeartRateAsync (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<DistanceResult>   GetDistanceAsync  (DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
