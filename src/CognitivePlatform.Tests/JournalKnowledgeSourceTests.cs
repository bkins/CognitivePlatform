using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Tests;

public class JournalKnowledgeSourceTests
{
    private readonly Mock<IJournalService> _journalServiceMock = new();
    private readonly Mock<IObjectStore>    _storeMock          = new();
    private readonly JournalKnowledgeSource _source;

    public JournalKnowledgeSourceTests()
    {
        _source = new JournalKnowledgeSource(_journalServiceMock.Object, _storeMock.Object);
    }

    // ================================================================
    // ListHeaders
    // ================================================================

    [Fact]
    public void ListHeaders_ReturnsJournalKindTypeOnHeader()
    {
        var entry = MakeEntry();
        SetupListAllEntries(entry);

        var results = _source.ListHeaders(fromUtc: null, toUtc: null);

        Assert.Single(results);
        Assert.Equal("Journal", results[0].Type);
    }

    [Fact]
    public void ListHeaders_SetsCreatedUtcFromEntryCreatedUtc()
    {
        var created = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var entry   = MakeEntry(entryCreatedUtc: created);
        SetupListAllEntries(entry);

        var results = _source.ListHeaders(fromUtc: null, toUtc: null);

        Assert.Equal(created, results[0].CreatedUtc);
    }

    [Fact]
    public void ListHeaders_SetsUpdatedUtcFromLatestRevisionCreatedUtc()
    {
        var revisionCreated = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
        var entry           = MakeEntry(revisionCreatedUtc: revisionCreated);
        SetupListAllEntries(entry);

        var results = _source.ListHeaders(fromUtc: null, toUtc: null);

        Assert.Equal(revisionCreated, results[0].UpdatedUtc);
    }

    [Fact]
    public void ListHeaders_PassesDateRangeToJournalService()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to   = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        SetupListAllEntries();

        _source.ListHeaders(from, to);

        _journalServiceMock.Verify(service => service.ListAllEntries(from, to), Times.Once);
    }

    // ================================================================
    // GetKnowledgeItems
    // ================================================================

    [Fact]
    public void GetKnowledgeItems_ReturnsItemsAcrossAllWorkspaces()
    {
        var entry = MakeEntry();
        SetupListAllEntries(entry);

        var results = _source.GetKnowledgeItems(new Api.KnowledgeInbox.KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(results);
    }

    [Fact]
    public void GetKnowledgeItems_UsesListAllEntries_NotListEntries()
    {
        SetupListAllEntries();

        _ = _source.GetKnowledgeItems(new Api.KnowledgeInbox.KnowledgeQuery(), CancellationToken.None).ToList();

        _journalServiceMock.Verify(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                                , It.IsAny<DateTimeOffset?>())
                                 , Times.Never);
        _journalServiceMock.Verify(service => service.ListAllEntries(It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>())
                                 , Times.Once);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static JournalEntryWithRevision MakeEntry( DateTimeOffset? entryCreatedUtc    = null
                                                      , DateTimeOffset? revisionCreatedUtc = null)
    {
        var id       = Guid.NewGuid().ToString("N");
        var entry    = new JournalEntry
                       {
                           Id         = id
                         , CreatedUtc = entryCreatedUtc ?? DateTimeOffset.UtcNow
                       };
        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = id
                         , CreatedUtc = revisionCreatedUtc ?? DateTimeOffset.UtcNow
                         , Text       = "Journal text"
                       };
        return new JournalEntryWithRevision(entry, revision, IsEdited: false);
    }

    private void SetupListAllEntries(params JournalEntryWithRevision[] entries)
    {
        _journalServiceMock.Setup(service => service.ListAllEntries(It.IsAny<DateTimeOffset?>()
                                                                  , It.IsAny<DateTimeOffset?>()))
                           .Returns(entries.ToList());
    }
}
