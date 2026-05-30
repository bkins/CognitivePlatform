namespace CognitivePlatform.Api.Wellbeing;

public interface IWellbeingSignalCollector
{
    /// <summary>
    /// Gathers wellbeing signals for <paramref name="date"/> from all four data sources
    /// (Health, Tasks, Journal, DailyRecord), persists them via <see cref="IWellbeingSignalStore"/>,
    /// and returns the full set. Health signals are omitted — not null — when the phone is unreachable.
    /// </summary>
    Task<IReadOnlyList<WellbeingSignal>> CollectSignalsAsync(
        DateOnly          date
      , CancellationToken cancellationToken = default);
}
