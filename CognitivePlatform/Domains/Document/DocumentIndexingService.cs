using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Integrations.Embeddings;
using CP.Shared.Primitives.Avails;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CognitivePlatform.Api.Domains.Document;

/// <summary>
/// Reads a file from disk, extracts text by type, chunks it, embeds each chunk,
/// and saves all resulting VectorEntry records under domain "document".
///
/// Supported file types: .txt, .md, .pdf
/// Limitation: PdfPig extracts text from machine-readable PDFs only.
/// Scanned PDFs (image-based) produce empty or garbled text — OCR is a future follow-up.
/// </summary>
public sealed class DocumentIndexingService : IDocumentIndexingService
{
    private const string DocumentDomain    = "document";
    private const string IndexPartition    = "indexed_documents";

    private readonly IEmbeddingService                   _embeddingService;
    private readonly IVectorStore                        _vectorStore;
    private readonly IObjectStore                        _objectStore;
    private readonly DocumentChunkingService             _chunkingService;
    private readonly ILogger<DocumentIndexingService>    _logger;

    public DocumentIndexingService( IEmbeddingService                 embeddingService
                                  , IVectorStore                      vectorStore
                                  , IObjectStore                      objectStore
                                  , DocumentChunkingService           chunkingService
                                  , ILogger<DocumentIndexingService>  logger )
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore      = vectorStore      ?? throw new ArgumentNullException(nameof(vectorStore));
        _objectStore      = objectStore      ?? throw new ArgumentNullException(nameof(objectStore));
        _chunkingService  = chunkingService  ?? throw new ArgumentNullException(nameof(chunkingService));
        _logger           = logger           ?? throw new ArgumentNullException(nameof(logger));
    }

    // -----------------------------------------------------------------------
    // IndexAsync
    // -----------------------------------------------------------------------

    public async Task<IndexedDocument?> IndexAsync(string filePath, CancellationToken ct = default)
    {
        if (filePath.HasNoValue())
            return null;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Document indexing skipped — file not found: {FilePath}", filePath);
            return null;
        }

        if (!_embeddingService.IsAvailable)
        {
            _logger.LogWarning("Document indexing skipped — embedding service is unavailable");
            return null;
        }

        var docId        = ComputeDocumentId(filePath);
        var lastModified = File.GetLastWriteTimeUtc(filePath);
        var existing     = _objectStore.Get<IndexedDocument>(docId, IndexPartition);

        if (existing is not null && existing.LastModified == lastModified)
        {
            _logger.LogDebug("Skipping re-index of '{FilePath}' — unchanged since {LastModified:O}", filePath, lastModified);
            return existing;
        }

        var text = await ExtractTextAsync(filePath, ct);
        if (text.HasNoValue())
        {
            _logger.LogWarning("Document indexing skipped — no text extracted from '{FilePath}'", filePath);
            return null;
        }

        var chunks    = _chunkingService.Chunk(text);
        var fileInfo  = new FileInfo(filePath);
        var chunkList = chunks.ToList();

        await _vectorStore.DeleteByReferenceAsync(DocumentDomain, docId, ct);

        for (int chunkIndex = 0; chunkIndex < chunkList.Count; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var embedding = await _embeddingService.EmbedAsync(chunkList[chunkIndex], ct);
                var entry     = new VectorEntry
                                {
                                    Id          = $"{DocumentDomain}:{docId}:{chunkIndex}"
                                  , Domain      = DocumentDomain
                                  , ReferenceId = docId
                                  , Text        = chunkList[chunkIndex]
                                  , Embedding   = embedding
                                };

                await _vectorStore.SaveAsync(entry, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Embedding failed for chunk {Index} of '{FilePath}'", chunkIndex, filePath);
            }
        }

        var document = new IndexedDocument
                       {
                           Id            = docId
                         , FilePath      = filePath
                         , FileName      = fileInfo.Name
                         , FileSizeBytes = fileInfo.Length
                         , ChunkCount    = chunkList.Count
                         , IndexedAt     = DateTime.UtcNow
                         , LastModified  = lastModified
                       };

        await _objectStore.Save(document, IndexPartition, docId);

        _logger.LogInformation(
            "Indexed '{FileName}' — {ChunkCount} chunk(s) embedded",
            document.FileName
          , document.ChunkCount);

        return document;
    }

    // -----------------------------------------------------------------------
    // DeleteAsync
    // -----------------------------------------------------------------------

    public async Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        var docId = ComputeDocumentId(filePath);

        await _vectorStore.DeleteByReferenceAsync(DocumentDomain, docId, ct);
        _objectStore.SoftDelete<IndexedDocument>(docId, IndexPartition);
    }

    // -----------------------------------------------------------------------
    // Text extraction per file type
    // -----------------------------------------------------------------------

    private static async Task<string> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".txt"  => await File.ReadAllTextAsync(filePath, ct)
          , ".md"   => StripMarkdown(await File.ReadAllTextAsync(filePath, ct))
          , ".pdf"  => ExtractPdfText(filePath)
          , _       => string.Empty
        };
    }

    private static string StripMarkdown(string markdown)
    {
        // Remove headings, bold, italic, code spans, links, images
        var text = Regex.Replace(markdown, RegexMatchingPatterns.MarkdownHeaderPattern,     string.Empty);
            text = Regex.Replace(text,     RegexMatchingPatterns.MarkdownEmphasisPattern,   "$1");
            text = Regex.Replace(text,     RegexMatchingPatterns.MarkdownInlineCodePattern, string.Empty);
            text = Regex.Replace(text,     RegexMatchingPatterns.MarkdownImageLinkPattern,  string.Empty);
            text = Regex.Replace(text,     RegexMatchingPatterns.MarkdownHyperlinkPattern,  "$1");
            
        return text.Trim();
    }

    /// <summary>
    /// Extracts machine-readable text from a PDF using PdfPig.
    /// Scanned (image-only) PDFs will return empty or garbled text — OCR is a future follow-up.
    /// </summary>
    private static string ExtractPdfText(string filePath)
    {
        try
        {
            using var pdf = PdfDocument.Open(filePath);
            var sb = new StringBuilder();

            foreach (Page page in pdf.GetPages())
                sb.AppendLine(page.Text);

            return sb.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    // -----------------------------------------------------------------------
    // Stable document ID — SHA-256 of the normalized file path
    // -----------------------------------------------------------------------

    private static string ComputeDocumentId(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).ToLowerInvariant();
        var hashBytes  = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
