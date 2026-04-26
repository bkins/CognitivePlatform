namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Controls throttling, output caps, and the conversation-history analysis window
/// for the Insight Engine. Bound from configuration so the four caps are tunable
/// without a rebuild.
/// </summary>
public sealed record InsightPolicy
{
    /// <summary>Hard cap on insights per conversation turn.</summary>
    public int MaxPerTurn { get; init; } = 2;

    /// <summary>
    /// Do not re-emit an insight with the same DeduplicationKey within this window.
    /// </summary>
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromHours(48);

    /// <summary>Per-category overrides for <see cref="RepeatWindow"/>.</summary>
    public Dictionary<InsightCategory, TimeSpan> CategoryRepeatWindows { get; init; } = new();

    /// <summary>
    /// Maximum number of conversation turns the reflection provider examines when
    /// building its prompt. Phase A degrades to last-turn-only because the
    /// platform does not yet keep a turn history; the cap is here for forward-compat.
    /// </summary>
    public int MaxAnalysisTurns { get; init; } = 10;

    /// <summary>
    /// Soft token budget for the reflection prompt. Whichever of
    /// <see cref="MaxAnalysisTurns"/> / <see cref="MaxAnalysisTokens"/> is hit first wins.
    /// </summary>
    public int MaxAnalysisTokens { get; init; } = 2000;

    public TimeSpan GetRepeatWindow(InsightCategory category)
        => CategoryRepeatWindows.TryGetValue(category, out var window) ? window : RepeatWindow;
}
