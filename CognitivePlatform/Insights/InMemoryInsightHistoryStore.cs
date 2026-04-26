using System.Collections.Concurrent;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase A in-memory history store. Process-scoped (singleton): restart clears
/// all history. Replaced by an Object-Store-backed implementation in Phase B.
///
/// Keyed on <see cref="Insight.DeduplicationKey"/>; same-key writes overwrite the
/// previous entry so repeat emissions advance the dedup window.
/// </summary>
public sealed class InMemoryInsightHistoryStore : IInsightHistoryStore
{
    private readonly ConcurrentDictionary<string, InsightHistoryItem> _items = new();

    public Task RecordEmittedAsync( IReadOnlyList<Insight> insights
                                  , CancellationToken      cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;

        foreach (var insight in insights)
        {
            if (string.IsNullOrWhiteSpace(insight.DeduplicationKey))
                continue;

            var item = new InsightHistoryItem(insight.DeduplicationKey
                                            , now
                                            , insight.Category
                                            , insight.Message);

            _items[insight.DeduplicationKey] = item;
        }

        return Task.CompletedTask;
    }

    public Task<bool> WasRecentlyEmittedAsync( string             deduplicationKey
                                             , TimeSpan           window
                                             , CancellationToken  cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(deduplicationKey))
            return Task.FromResult(false);

        if (_items.TryGetValue(deduplicationKey, out var item).Equals(false))
            return Task.FromResult(false);

        var age = DateTime.UtcNow - item!.EmittedAt;
        return Task.FromResult(age <= window);
    }

    public Task<IReadOnlyList<InsightHistoryItem>> GetRecentAsync(
        TimeSpan          window
      , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cutoff = DateTime.UtcNow - window;

        IReadOnlyList<InsightHistoryItem> recent = _items.Values
                                                         .Where(item => item.EmittedAt >= cutoff)
                                                         .OrderByDescending(item => item.EmittedAt)
                                                         .ToList();

        return Task.FromResult(recent);
    }
}
