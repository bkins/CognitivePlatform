namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Controls throttling and output caps for the Insight Engine.
/// </summary>
public sealed class InsightPolicy
{
    /// <summary>Hard cap on insights per conversation turn.</summary>
    public int MaxPerTurn { get; init; } = 2;

    /// <summary>Do not re-emit an insight with the same DeduplicationKey within this window.</summary>
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromHours(48);

    /// <summary>Per-category overrides for RepeatWindow.</summary>
    public Dictionary<InsightCategory, TimeSpan> CategoryRepeatWindows { get; init; } = new();

    public TimeSpan GetRepeatWindow(InsightCategory category)
        => CategoryRepeatWindows.TryGetValue(category, out var window) ? window : RepeatWindow;
}
