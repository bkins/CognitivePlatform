using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.KnowledgeInbox;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class ConversationKnowledgeSourceTests
{
    private readonly Mock<IObjectStore>         _objectStoreMock;
    private readonly Mock<IConversationService> _serviceMock;
    private readonly ConversationKnowledgeSource _source;

    public ConversationKnowledgeSourceTests()
    {
        _objectStoreMock = new Mock<IObjectStore>();
        _serviceMock     = new Mock<IConversationService>();

        _source = new ConversationKnowledgeSource(
            _objectStoreMock.Object,
            _serviceMock.Object);
    }

    [Fact]
    public void Kind_ReturnsConversation()
    {
        Assert.Equal(KnowledgeKind.Conversation, _source.Kind);
    }

    [Fact]
    public void GetKnowledgeItems_ReturnsMappedItems_WithAnalysisSummaryAndTopics()
    {
        var conversationId = Guid.NewGuid();
        var record = new ConversationRecord
        {
            Id             = conversationId
          , Title          = "Sprint Planning"
          , RecordedAtUtc  = DateTime.UtcNow
        };
        var analysis = new ConversationAnalysis
        {
            ConversationId = conversationId
          , Summary        = "Planned sprint tasks and backlog items."
          , Topics         = new List<AnalysisDerivedItem>
            {
                new() { Content = "Sprint Backlog" }
              , new() { Content = "Release Date" }
            }
        };

        _objectStoreMock.Setup(store => store.List<ConversationRecord>(null, null, null))
                        .Returns(new List<ConversationRecord> { record });
        _objectStoreMock.Setup(store => store.Get<ConversationAnalysis>($"analysis_{conversationId}", null))
                        .Returns(analysis);

        var items = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(items);
        var item = items[0];
        Assert.Equal(conversationId, item.Id);
        Assert.Equal(KnowledgeKind.Conversation, item.Kind);
        Assert.Equal("Sprint Planning", item.Title);
        Assert.Equal("Planned sprint tasks and backlog items.", item.Summary);
        Assert.Equal(KnowledgeStatus.Active, item.Status);
        Assert.Equal(2, item.Tags.Count());
    }

    [Fact]
    public void ListHeaders_ReturnsNonDeletedHeaders()
    {
        var activeId  = Guid.NewGuid();
        var deletedId = Guid.NewGuid();

        var records = new List<ConversationRecord>
        {
            new() { Id = activeId, Title = "Active", IsDeleted = false, RecordedAtUtc = DateTime.UtcNow }
          , new() { Id = deletedId, Title = "Deleted", IsDeleted = true, RecordedAtUtc = DateTime.UtcNow }
        };

        _objectStoreMock.Setup(store => store.List<ConversationRecord>(null, null, null))
                        .Returns(records);

        var headers = _source.ListHeaders(null, null);

        Assert.Single(headers);
        Assert.Equal(activeId.ToString(), headers[0].Id);
        Assert.Equal("Conversation", headers[0].Type);
    }

    [Fact]
    public void Archive_DelegatesToConversationServiceDelete()
    {
        var conversationId = Guid.NewGuid();
        _serviceMock.Setup(service => service.DeleteRecordingAsync(conversationId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);

        _source.Archive(conversationId, CancellationToken.None);

        _serviceMock.Verify(service => service.DeleteRecordingAsync(conversationId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
