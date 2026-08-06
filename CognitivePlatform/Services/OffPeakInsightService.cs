using System;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Services;

/// <summary>
/// Executes cross-domain insight generation during off-peak windows (nightly batch processing)
/// so interactive conversations remain responsive and unburdened by heavy historical correlations.
/// </summary>
public sealed class OffPeakInsightService : BackgroundService
{
    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly ILogger<OffPeakInsightService> _logger;

    public OffPeakInsightService( IServiceScopeFactory           scopeFactory
                                , ILogger<OffPeakInsightService> logger )
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);
                await RunInsightPassAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Executes a single pass of insight generation across all providers.
    /// Exposes an entry point for testing and manual triggering.
    /// </summary>
    public async Task<int> RunInsightPassAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            using var scope    = _scopeFactory.CreateScope();
            var       engine   = scope.ServiceProvider.GetRequiredService<IInsightEngine>();
            var       context  = new ConversationContext("off-peak-batch");
            var       insights = await engine.GenerateInsightsAsync(context, stoppingToken).ConfigureAwait(false);

            var count = insights?.Count ?? 0;
            _logger.LogInformation("OffPeakInsightService: completed insight generation pass, yielded {Count} insights.", count);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OffPeakInsightService: unhandled error during off-peak insight generation.");
            return 0;
        }
    }
}
