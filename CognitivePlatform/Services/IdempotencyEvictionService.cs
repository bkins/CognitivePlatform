using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Services;

/// <summary>
/// Background service that prunes idempotency cache entries older than
/// <see cref="TtlDays"/> days on startup and then once every <see cref="RunInterval"/>.
///
/// Idempotency records are ephemeral by design (dedup window is short-lived),
/// so hard-deleting them does not violate the soft-delete invariant that governs
/// domain objects. This keeps <c>platform.db</c> from growing unboundedly.
/// </summary>
public sealed class IdempotencyEvictionService : IHostedService, IDisposable
{
    private readonly SqliteObjectStore _store;
    private readonly ILogger<IdempotencyEvictionService> _logger;
    private Timer? _timer;

    private const int  TtlDays     = 7;
    private static readonly TimeSpan RunInterval = TimeSpan.FromDays(1);

    public IdempotencyEvictionService( SqliteObjectStore                    store
                                     , ILogger<IdempotencyEvictionService>  logger )
    {
        _store  = store  ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(Evict, state: null, dueTime: TimeSpan.Zero, period: RunInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private void Evict(object? state)
    {
        try
        {
            var deleted = _store.DeleteOlderThan<ProcessedRequest>(TimeSpan.FromDays(TtlDays));

            if (deleted > 0)
                _logger.LogInformation("Idempotency eviction: removed {Count} records older than {TtlDays} days."
                                      , deleted, TtlDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency eviction failed.");
        }
    }
}
