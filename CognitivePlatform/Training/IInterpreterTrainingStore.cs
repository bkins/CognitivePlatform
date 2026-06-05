namespace CognitivePlatform.Api.Training;

public interface IInterpreterTrainingStore
{
    Task SaveAsync(InterpreterTrainingRecord record, CancellationToken ct = default);
    Task<IList<InterpreterTrainingRecord>> GetRecentAsync(int count = 100, CancellationToken ct = default);
    Task<IList<InterpreterTrainingRecord>> GetForExportAsync(int limit, bool succeededOnly, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
    Task<TrainingCorpusStats> GetStatsAsync(CancellationToken ct = default);
}
