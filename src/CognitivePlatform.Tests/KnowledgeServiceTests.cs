using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Tests;

public class KnowledgeServiceTests
{
    private readonly Mock<IObjectStore> _storeMock = new();

    private static KnowledgeItemDto MakeItem( KnowledgeKind   kind
                                             , DateTimeOffset   lastModifiedAt)
        => new()
           {
                   Id             = Guid.NewGuid()
                 , Kind           = kind
                 , Title          = "Item"
                 , LastModifiedAt = lastModifiedAt
                 , Status         = KnowledgeStatus.Active
           };

    private static Mock<IKnowledgeSource> MakeSource( KnowledgeKind                kind
                                                     , IEnumerable<KnowledgeItemDto> items)
    {
        var mock = new Mock<IKnowledgeSource>();
        mock.Setup(source => source.Kind).Returns(kind);
        mock.Setup(source => source.GetKnowledgeItems(It.IsAny<KnowledgeQuery>()
                                                    , It.IsAny<CancellationToken>()))
            .Returns(items);
        return mock;
    }

    private static ObjectHeader MakeHeader( KnowledgeKind   kind
                                           , DateTimeOffset  createdUtc
                                           , string?         id = null)
        => new ObjectHeader( id ?? Guid.NewGuid().ToString("N")
                           , kind.ToString()
                           , createdUtc
                           , createdUtc);

    private static Mock<IKnowledgeSource> MakeSourceWithHeaders( KnowledgeKind              kind
                                                                 , IEnumerable<ObjectHeader>  headers)
    {
        var mock = new Mock<IKnowledgeSource>();
        mock.Setup(source => source.Kind).Returns(kind);
        mock.Setup(source => source.GetKnowledgeItems(It.IsAny<KnowledgeQuery>()
                                                    , It.IsAny<CancellationToken>()))
            .Returns(Enumerable.Empty<KnowledgeItemDto>());
        mock.Setup(source => source.ListHeaders(It.IsAny<DateTimeOffset?>()
                                               , It.IsAny<DateTimeOffset?>()))
            .Returns(headers.ToList());
        return mock;
    }

    // ================================================================
    // AGGREGATION
    // ================================================================

    [Fact]
    public void GetKnowledge_AggregatesItemsFromAllSources_WhenKindFilterIsNull()
    {
        var journalItem = MakeItem(KnowledgeKind.Journal, DateTimeOffset.UtcNow.AddHours(-1));
        var taskItem    = MakeItem(KnowledgeKind.Task,    DateTimeOffset.UtcNow.AddHours(-2));

        var journalSource = MakeSource(KnowledgeKind.Journal, new[] { journalItem });
        var taskSource    = MakeSource(KnowledgeKind.Task,    new[] { taskItem    });

        var service = new KnowledgeService(new[] { journalSource.Object, taskSource.Object }
                                         , _storeMock.Object);

        var results = service.GetKnowledge(new KnowledgeQuery(), CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    // ================================================================
    // KIND FILTER
    // ================================================================

    [Fact]
    public void GetKnowledge_ReturnsOnlyItemsFromMatchingSource_WhenKindFilterIsSet()
    {
        var journalItem = MakeItem(KnowledgeKind.Journal, DateTimeOffset.UtcNow.AddHours(-1));
        var taskItem    = MakeItem(KnowledgeKind.Task,    DateTimeOffset.UtcNow.AddHours(-2));

        var journalSource = MakeSource(KnowledgeKind.Journal, new[] { journalItem });
        var taskSource    = MakeSource(KnowledgeKind.Task,    new[] { taskItem    });

        var service = new KnowledgeService(new[] { journalSource.Object, taskSource.Object }
                                         , _storeMock.Object);

        var query   = new KnowledgeQuery { Kind = KnowledgeKind.Task };
        var results = service.GetKnowledge(query, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(KnowledgeKind.Task, results[0].Kind);
    }

    // ================================================================
    // ListHeaders — AGGREGATION
    // ================================================================

    [Fact]
    public void ListHeaders_AggregatesHeadersFromAllSources_WhenTypeIsEmpty()
    {
        var journalHeader = MakeHeader(KnowledgeKind.Journal, DateTimeOffset.UtcNow.AddHours(-1));
        var taskHeader    = MakeHeader(KnowledgeKind.Task,    DateTimeOffset.UtcNow.AddHours(-2));

        var journalSource = MakeSourceWithHeaders(KnowledgeKind.Journal, new[] { journalHeader });
        var taskSource    = MakeSourceWithHeaders(KnowledgeKind.Task,    new[] { taskHeader    });

        var service = new KnowledgeService(new[] { journalSource.Object, taskSource.Object }
                                         , _storeMock.Object);

        var results = service.ListHeaders(string.Empty);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ListHeaders_ReturnsOnlyMatchingTypeHeaders_WhenTypeIsSet()
    {
        var journalHeader = MakeHeader(KnowledgeKind.Journal, DateTimeOffset.UtcNow);
        var taskHeader    = MakeHeader(KnowledgeKind.Task,    DateTimeOffset.UtcNow);

        var journalSource = MakeSourceWithHeaders(KnowledgeKind.Journal, new[] { journalHeader });
        var taskSource    = MakeSourceWithHeaders(KnowledgeKind.Task,    new[] { taskHeader    });

        var service = new KnowledgeService(new[] { journalSource.Object, taskSource.Object }
                                         , _storeMock.Object);

        var results = service.ListHeaders("task");

        Assert.Single(results);
        Assert.Equal("Task", results[0].Type);
    }

    [Fact]
    public void ListHeaders_ReturnsEmpty_WhenTypeMatchesNoSource()
    {
        var source = MakeSourceWithHeaders(KnowledgeKind.Journal, new[] { MakeHeader(KnowledgeKind.Journal, DateTimeOffset.UtcNow) });

        var service = new KnowledgeService(new[] { source.Object }, _storeMock.Object);

        var results = service.ListHeaders("unknown");

        Assert.Empty(results);
    }

    [Fact]
    public void ListHeaders_OrdersByCreatedUtcDescending()
    {
        var oldest = MakeHeader(KnowledgeKind.Task, DateTimeOffset.UtcNow.AddDays(-3));
        var newest = MakeHeader(KnowledgeKind.Task, DateTimeOffset.UtcNow.AddDays(-1));
        var middle = MakeHeader(KnowledgeKind.Task, DateTimeOffset.UtcNow.AddDays(-2));

        var source = MakeSourceWithHeaders(KnowledgeKind.Task, new[] { oldest, newest, middle });

        var service = new KnowledgeService(new[] { source.Object }, _storeMock.Object);

        var results = service.ListHeaders(string.Empty);

        Assert.Equal(3,         results.Count);
        Assert.Equal(newest.Id, results[0].Id);
        Assert.Equal(middle.Id, results[1].Id);
        Assert.Equal(oldest.Id, results[2].Id);
    }

    [Fact]
    public void ListHeaders_PassesDateRangeThroughToSources()
    {
        var from   = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to     = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var source = MakeSourceWithHeaders(KnowledgeKind.Task, Enumerable.Empty<ObjectHeader>());

        var service = new KnowledgeService(new[] { source.Object }, _storeMock.Object);

        service.ListHeaders(string.Empty, from, to);

        source.Verify(src => src.ListHeaders(from, to), Times.Once);
    }

    // ================================================================
    // ORDERING
    // ================================================================

    [Fact]
    public void GetKnowledge_OrdersByLastModifiedAtDescending()
    {
        var oldest  = MakeItem(KnowledgeKind.Task, DateTimeOffset.UtcNow.AddDays(-3));
        var newest  = MakeItem(KnowledgeKind.Task, DateTimeOffset.UtcNow.AddDays(-1));
        var middle  = MakeItem(KnowledgeKind.Task, DateTimeOffset.UtcNow.AddDays(-2));

        var source = MakeSource(KnowledgeKind.Task, new[] { oldest, newest, middle });

        var service = new KnowledgeService(new[] { source.Object }, _storeMock.Object);

        var results = service.GetKnowledge(new KnowledgeQuery(), CancellationToken.None);

        Assert.Equal(3,                results.Count);
        Assert.Equal(newest.Id,        results[0].Id);
        Assert.Equal(middle.Id,        results[1].Id);
        Assert.Equal(oldest.Id,        results[2].Id);
    }
}
