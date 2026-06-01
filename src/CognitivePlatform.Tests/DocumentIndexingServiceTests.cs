using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Document;
using CognitivePlatform.Api.Integrations.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class DocumentIndexingServiceTests : IDisposable
{
    private readonly Mock<IEmbeddingService>          _embeddingMock  = new();
    private readonly Mock<IVectorStore>               _vectorStoreMock = new();
    private readonly Mock<IObjectStore>               _objectStoreMock = new();
    private readonly DocumentChunkingService          _chunkingService = new();
    private readonly DocumentIndexingService          _service;

    private readonly List<string> _tempFiles = new();

    public DocumentIndexingServiceTests()
    {
        _service = new DocumentIndexingService(
            _embeddingMock.Object
          , _vectorStoreMock.Object
          , _objectStoreMock.Object
          , _chunkingService
          , NullLogger<DocumentIndexingService>.Instance);

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync(It.IsAny<string>(), default))
                      .ReturnsAsync(new float[] { 1f, 0f });

        _objectStoreMock.Setup(store => store.Get<IndexedDocument>(It.IsAny<string>(), It.IsAny<string>()))
                        .Returns((IndexedDocument?)null);

        _objectStoreMock.Setup(store => store.Save(It.IsAny<IndexedDocument>()
                                                  , It.IsAny<string>()
                                                  , It.IsAny<string>()))
                        .ReturnsAsync("saved-id");
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); }
            catch { /* ignore */ }
        }
    }

    private string CreateTempFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"doc_test_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    // ================================================================
    // IndexAsync — returns null when file not found
    // ================================================================

    [Fact]
    public async Task IndexAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var result = await _service.IndexAsync(@"C:\nonexistent\file.txt");

        Assert.Null(result);
    }

    // ================================================================
    // IndexAsync — returns null when embedding service unavailable
    // ================================================================

    [Fact]
    public async Task IndexAsync_ReturnsNull_WhenEmbeddingServiceUnavailable()
    {
        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(false);
        var path = CreateTempFile(".txt", "Some content.");

        var result = await _service.IndexAsync(path);

        Assert.Null(result);
    }

    // ================================================================
    // IndexAsync — indexes a .txt file
    // ================================================================

    [Fact]
    public async Task IndexAsync_IndexesTxtFile_AndReturnsDocument()
    {
        var path = CreateTempFile(".txt", "This is a test document about software engineering.");

        var result = await _service.IndexAsync(path);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFileName(path), result.FileName);
        Assert.True(result.ChunkCount >= 1);
        _vectorStoreMock.Verify(store => store.SaveAsync(It.IsAny<VectorEntry>(), default), Times.AtLeastOnce);
    }

    // ================================================================
    // IndexAsync — indexes a .md file (strips markdown)
    // ================================================================

    [Fact]
    public async Task IndexAsync_IndexesMdFile_AndReturnsDocument()
    {
        var content = "# Heading\n\nThis is a **markdown** document.";
        var path    = CreateTempFile(".md", content);

        var result = await _service.IndexAsync(path);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFileName(path), result.FileName);
    }

    // ================================================================
    // IndexAsync — skips re-index when LastModified unchanged
    // ================================================================

    [Fact]
    public async Task IndexAsync_SkipsReIndex_WhenFileUnchanged()
    {
        var path         = CreateTempFile(".txt", "Unchanged content.");
        var lastModified = File.GetLastWriteTimeUtc(path);
        var existing     = new IndexedDocument
                           {
                               Id           = "abc123"
                             , FilePath     = path
                             , FileName     = Path.GetFileName(path)
                             , ChunkCount   = 1
                             , LastModified = lastModified
                           };

        _objectStoreMock.Setup(store => store.Get<IndexedDocument>(It.IsAny<string>(), It.IsAny<string>()))
                        .Returns(existing);

        var result = await _service.IndexAsync(path);

        Assert.NotNull(result);
        _vectorStoreMock.Verify(store => store.SaveAsync(It.IsAny<VectorEntry>(), default), Times.Never);
    }

    // ================================================================
    // IndexAsync — returns null for unsupported extension
    // ================================================================

    [Fact]
    public async Task IndexAsync_ReturnsNull_ForUnsupportedExtension()
    {
        var path = CreateTempFile(".docx", "Some content");

        var result = await _service.IndexAsync(path);

        Assert.Null(result);
    }

    // ================================================================
    // DeleteAsync — calls DeleteByReferenceAsync and SoftDelete
    // ================================================================

    [Fact]
    public async Task DeleteAsync_PurgesVectorsAndSoftDeletesDocument()
    {
        var path = CreateTempFile(".txt", "to be deleted");

        await _service.DeleteAsync(path);

        _vectorStoreMock.Verify(
            store => store.DeleteByReferenceAsync("document", It.IsAny<string>(), default)
          , Times.Once);

        _objectStoreMock.Verify(
            store => store.SoftDelete<IndexedDocument>(It.IsAny<string>(), "indexed_documents")
          , Times.Once);
    }
}
