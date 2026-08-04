using CognitivePlatform.Api.Integrations.Health.Models;

namespace CognitivePlatform.Api.Integrations.Health;

/// <summary>
/// Implements <see cref="IHealthProvider"/> by reading from the push-populated
/// <see cref="HealthDataCache"/> rather than making HTTP calls to the phone.
/// <para>
/// <see cref="IsConnected"/> returns <see langword="true"/> when today's snapshot
/// is present and within the cache TTL.  Each data method returns the cached value
/// for the requested date, or throws <see cref="HealthProviderException"/> (404) if
/// no data has been pushed yet for that date.
/// </para>
/// </summary>
public sealed class CacheBackedHealthProvider : IHealthProvider
{
    private readonly HealthDataCache _cache;

    public CacheBackedHealthProvider(HealthDataCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc/>
    public bool IsConnected
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return _cache.TryGet(today, out _);
        }
    }

    /// <inheritdoc/>
    public Task<StepCountResult> GetStepCountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var snapshot = Require(from);

        return Task.FromResult(new StepCountResult
                               {
                                   Steps          = snapshot.Steps
                                 , DistanceMetres = snapshot.DistanceMetres > 0 ? snapshot.DistanceMetres : null
                               });
    }

    /// <inheritdoc/>
    public Task<SleepResult> GetSleepAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var snapshot = Require(from);

        return Task.FromResult(new SleepResult
                               {
                                   TotalMinutes = snapshot.SleepMinutes
                                 , Sessions     = snapshot.SleepSessions
                               });
    }

    /// <inheritdoc/>
    public Task<HeartRateResult> GetHeartRateAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var snapshot = Require(from);

        return Task.FromResult(new HeartRateResult
                               {
                                   AverageBpm = snapshot.AverageHeartRate
                                 , MinBpm     = snapshot.MinHeartRate > 0 ? snapshot.MinHeartRate : null
                                 , MaxBpm     = snapshot.MaxHeartRate > 0 ? snapshot.MaxHeartRate : null
                               });
    }

    /// <inheritdoc/>
    public Task<DistanceResult> GetDistanceAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var snapshot = Require(from);

        return Task.FromResult(new DistanceResult
                               {
                                   Metres = snapshot.DistanceMetres
                                 , Steps  = snapshot.Steps > 0 ? snapshot.Steps : null
                               });
    }

    // -----------------------------------------------------------------------

    private HealthSnapshot Require(DateTimeOffset from)
    {
        var date = DateOnly.FromDateTime(from.LocalDateTime.Date);

        if (_cache.TryGet(date, out var snapshot))
            return snapshot!;

        throw new HealthProviderException(System.Net.HttpStatusCode.NotFound
                                        , $"No health data cached for {date:yyyy-MM-dd}.");
    }
}
