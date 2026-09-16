namespace CognitivePlatform.Api.Data;

public interface IHistoricalObjectWriter
{
    Task<string> SaveHistorical<T>(T value, DateTimeOffset createdUtc, string? partitionKey = null, string? id = null);
}
