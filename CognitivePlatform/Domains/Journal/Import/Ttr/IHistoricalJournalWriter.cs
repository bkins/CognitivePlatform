namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public interface IHistoricalJournalWriter
{
    Task<HistoricalJournalWriteResult> CreateAsync(HistoricalJournalWriteRequest request);
}
