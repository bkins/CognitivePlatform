using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;

namespace CognitivePlatform.Tests;

public class JournalRevisionRepositoryTests
{
    private readonly Mock<IObjectStore>     _storeMock = new();
    private readonly JournalRevisionRepository _repository;

    public JournalRevisionRepositoryTests()
    {
        _repository = new JournalRevisionRepository(_storeMock.Object);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRevisionsByEntryId_ThrowsArgumentException_WhenEntryIdIsNullOrEmptyOrWhitespace(string? entryId)
    {
        Assert.Throws<ArgumentException>(() => _repository.GetRevisionsByEntryId(entryId!));
    }

    [Fact]
    public void GetRevisionsByEntryId_ReturnsRevisionsOrderedByCreatedUtcDescending_WhenEntryIdMatches()
    {
        var targetEntryId = Guid.NewGuid().ToString("N");
        var otherEntryId  = Guid.NewGuid().ToString("N");

        var olderRevision = new JournalRevision
                            {
                                RevisionId = Guid.NewGuid().ToString("N")
                              , EntryId    = targetEntryId
                              , CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                              , Text       = "Older revision text"
                            };

        var newerRevision = new JournalRevision
                            {
                                RevisionId = Guid.NewGuid().ToString("N")
                              , EntryId    = targetEntryId
                              , CreatedUtc = DateTimeOffset.UtcNow
                              , Text       = "Newer revision text"
                            };

        var otherRevision = new JournalRevision
                            {
                                RevisionId = Guid.NewGuid().ToString("N")
                              , EntryId    = otherEntryId
                              , CreatedUtc = DateTimeOffset.UtcNow
                              , Text       = "Other entry revision text"
                            };

        _storeMock.Setup(store => store.List<JournalRevision>(null, null, null))
                  .Returns(new List<JournalRevision> { olderRevision, newerRevision, otherRevision });

        var results = _repository.GetRevisionsByEntryId(targetEntryId);

        Assert.Equal(2, results.Count);
        Assert.Equal(newerRevision.RevisionId, results[0].RevisionId);
        Assert.Equal(olderRevision.RevisionId, results[1].RevisionId);
    }

    [Fact]
    public void GetRevisionsByEntryId_ReturnsEmptyList_WhenNoRevisionsMatchEntryId()
    {
        var entryId = Guid.NewGuid().ToString("N");
        _storeMock.Setup(store => store.List<JournalRevision>(null, null, null))
                  .Returns(new List<JournalRevision>());

        var results = _repository.GetRevisionsByEntryId(entryId);

        Assert.Empty(results);
    }
}
