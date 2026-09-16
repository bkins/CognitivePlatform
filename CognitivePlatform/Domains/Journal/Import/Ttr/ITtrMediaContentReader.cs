namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public interface ITtrMediaContentReader
{
    Task<byte[]> ReadAsync(TtrImportPlanRequest source, TtrPlannedMedia media, CancellationToken cancellationToken = default);
}
