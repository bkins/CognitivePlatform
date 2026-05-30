namespace CognitivePlatform.Api.Wellbeing;

public interface IWellbeingSignalStore
{
    Task SaveSignalAsync(WellbeingSignal signal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WellbeingSignal>> GetSignalsAsync(
        DateTimeOffset    from
      , DateTimeOffset    to
      , CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WellbeingSignal>> GetSignalsForDateAsync(
        DateOnly          date
      , CancellationToken cancellationToken = default);
}
