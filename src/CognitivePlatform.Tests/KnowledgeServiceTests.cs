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
