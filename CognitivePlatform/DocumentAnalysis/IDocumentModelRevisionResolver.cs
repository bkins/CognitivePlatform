namespace CognitivePlatform.Api.DocumentAnalysis;

public interface IDocumentModelRevisionResolver
{
    Task<string?> ResolveAsync(string endpoint, string model, CancellationToken cancellationToken);
}
