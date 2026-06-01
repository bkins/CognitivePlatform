namespace CognitivePlatform.Api.Domains.Document;

public interface IDocumentIndexingService
{
    Task<IndexedDocument?> IndexAsync   (string filePath, CancellationToken ct = default);
    Task                   DeleteAsync  (string filePath, CancellationToken ct = default);
}
