using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Document;

[Domain(typeof(DocumentDomain))]
public class DocumentActions
{
    private const string DocumentDomain = "document";
    private const string IndexPartition = "indexed_documents";
    private const int    DefaultTopK    = 5;
    private const int    SnippetLength  = 150;

    private readonly IDocumentIndexingService _indexingService;
    private readonly IVectorStore             _vectorStore;
    private readonly IEmbeddingService        _embeddingService;
    private readonly IObjectStore             _objectStore;

    public DocumentActions( IDocumentIndexingService indexingService
                          , IVectorStore             vectorStore
                          , IEmbeddingService        embeddingService
                          , IObjectStore             objectStore )
    {
        _indexingService  = indexingService  ?? throw new ArgumentNullException(nameof(indexingService));
        _vectorStore      = vectorStore      ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _objectStore      = objectStore      ?? throw new ArgumentNullException(nameof(objectStore));
    }

    // -----------------------------------------------------------------------
    // IndexDocument
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Indexes a file on disk so its content becomes searchable via semantic search. Supported formats: .txt, .md, .pdf (machine-readable only)."
      , Examples    = new[]
        {
                "Index the file C:\\Notes\\journal.md"
              , "Add C:\\Documents\\report.pdf to the index"
              , "Index document C:\\work\\notes.txt"
        }
      , Category = "document"
    )]
    public async Task<string> IndexDocument(
        [NaturalLanguageParam(Description = "The full path to the file to index, e.g. 'C:\\Notes\\journal.md'.")]
        string filePath )
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Please provide the full path to the file you want to index.";

        if (!_embeddingService.IsAvailable)
            return "Document indexing isn't available yet — make sure Ollama is running with the nomic-embed-text model.";

        try
        {
            var document = await _indexingService.IndexAsync(filePath);

            if (document is null)
                return !File.Exists(filePath)
                           ? $"File not found: '{filePath}'."
                           : $"Could not extract text from '{filePath}'. Ensure it is a supported format (.txt, .md, or machine-readable .pdf).";

            return $"Indexed '{document.FileName}' — {document.ChunkCount} chunk(s) embedded, {FormatBytes(document.FileSizeBytes)}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return $"Indexing failed for '{filePath}'. Make sure the file exists and Ollama is running.";
        }
    }

    // -----------------------------------------------------------------------
    // SearchDocuments
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Semantically searches all indexed documents for content related to the query."
      , Examples    = new[]
        {
                "Search documents for project timeline"
              , "Find indexed files about quarterly goals"
              , "Search my documents for budget"
        }
      , Category = "document"
    )]
    public async Task<string> SearchDocuments(
        [NaturalLanguageParam(Description = "The topic or concept to search for across all indexed documents.")]
        string query )
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Please provide a search query.";

        if (!_embeddingService.IsAvailable)
            return "Document search isn't available yet — make sure Ollama is running with the nomic-embed-text model.";

        try
        {
            var embedding = await _embeddingService.EmbedAsync(query);
            var results   = await _vectorStore.SearchAsync(embedding, topK: DefaultTopK, domain: DocumentDomain);

            if (results.Count == 0)
                return $"No indexed documents match '{query}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Document search results for '{query}':");
            sb.AppendLine();

            foreach (var result in results)
            {
                var docRecord = _objectStore.Get<IndexedDocument>(result.Entry.ReferenceId, IndexPartition);
                var fileName  = docRecord?.FileName ?? result.Entry.ReferenceId;
                var snippet   = result.Entry.Text.Length <= SnippetLength
                                    ? result.Entry.Text
                                    : result.Entry.Text[..SnippetLength] + "…";

                sb.AppendLine($"[{fileName}] (score: {result.Score:F2})");
                sb.AppendLine($"  {snippet}");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return "Document search encountered an error. Make sure Ollama is running with the nomic-embed-text model.";
        }
    }

    // -----------------------------------------------------------------------
    // ListIndexedDocuments
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Lists all files currently indexed for semantic search."
      , Examples    = new[]
        {
                "List indexed documents"
              , "What files have been indexed?"
              , "Show me my indexed documents"
        }
      , Category = "document"
    )]
    public string ListIndexedDocuments()
    {
        var documents = _objectStore.List<IndexedDocument>(IndexPartition);

        if (documents.Count == 0)
            return "No documents have been indexed yet. Use 'index document <path>' to add a file.";

        var sb = new StringBuilder();
        sb.AppendLine($"Indexed documents ({documents.Count}):");
        sb.AppendLine();

        foreach (var document in documents.OrderBy(doc => doc.FileName, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"• {document.FileName} — {document.ChunkCount} chunk(s), {FormatBytes(document.FileSizeBytes)}, indexed {document.IndexedAt:yyyy-MM-dd}");
            sb.AppendLine($"  {document.FilePath}");
        }

        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)         return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
