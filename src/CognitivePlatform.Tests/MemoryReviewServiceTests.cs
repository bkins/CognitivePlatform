using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.MemoryReview;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class MemoryReviewServiceTests
{
    private readonly Mock<IIdentityService>     _identityServiceMock     = new();
    private readonly Mock<IPersonaService>      _personaServiceMock      = new();
    private readonly Mock<IPersonaStore>        _personaStoreMock        = new();
    private readonly Mock<IConversationService> _conversationServiceMock = new();
    private readonly Mock<IKnowledgeService>    _knowledgeServiceMock    = new();
    private readonly MemoryReviewService        _service;

    public MemoryReviewServiceTests()
    {
        _service = new MemoryReviewService(_identityServiceMock.Object
                                         , _personaServiceMock.Object
                                         , _personaStoreMock.Object
                                         , _conversationServiceMock.Object
                                         , _knowledgeServiceMock.Object);
    }

    [Fact]
    public async Task GetReviewAsync_ProjectsOwnedSourcesWithoutChangingTheirState()
    {
        var personaId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _identityServiceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([
                                new IdentityAssertion { Id = "assertion-1", Statement = "Prefers focused work", UserConfirmed = false, Confidence = 0.8, FirstObserved = now.AddDays(-2), LastReinforced = now }
                            ]);
        _personaServiceMock.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                           .ReturnsAsync([
                               new CanonicalPersona { Id = personaId, Name = "Avery" }
                           ]);
        _personaStoreMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                         .ReturnsAsync([
                             new PersonaMemory { Id = Guid.NewGuid(), PersonaId = personaId, Content = "Values directness", State = MemoryState.Canonical, Confidence = 0.9f, Source = MemorySource.UserFragment, LastModifiedUtc = now }
                         ]);
        _conversationServiceMock.Setup(service => service.ListRecordingsAsync(It.IsAny<CancellationToken>()))
                                .ReturnsAsync([
                                    new ConversationRecord { Id = conversationId, Title = "Planning session", RecordedAtUtc = now }
                                ]);
        _conversationServiceMock.Setup(service => service.GetMemoriesAsync(conversationId, It.IsAny<CancellationToken>()))
                                .ReturnsAsync([
                                    new ConversationMemoryCandidate { Id = Guid.NewGuid(), ConversationId = conversationId, Content = "Follow up on the plan", State = MemoryState.Provisional, Confidence = 0.7, ExtractedAtUtc = now }
                                ]);
        _knowledgeServiceMock.Setup(service => service.GetKnowledge(It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
                             .Returns([
                                 new KnowledgeItemDto { Id = Guid.NewGuid(), Kind = KnowledgeKind.Conversation, Title = "Planning notes", Summary = "Actionable summary", Status = KnowledgeStatus.Active, CreatedAt = now, LastModifiedAt = now }
                             ]);

        var result = await _service.GetReviewAsync(CancellationToken.None);

        Assert.Equal(4, result.Items.Count);
        Assert.Contains(result.Items, item => item.SourceKind == MemoryReviewSourceKind.Identity && item.State == MemoryReviewState.NeedsReview);
        Assert.Contains(result.Items, item => item.SourceKind == MemoryReviewSourceKind.Persona && item.State == MemoryReviewState.Confirmed);
        Assert.Contains(result.Items, item => item.SourceKind == MemoryReviewSourceKind.Conversation && item.State == MemoryReviewState.NeedsReview);
        Assert.Contains(result.Items, item => item.SourceKind == MemoryReviewSourceKind.Knowledge && item.State == MemoryReviewState.Confirmed);
        Assert.All(result.Items, item => Assert.Empty(item.AvailableOperations));
    }

    [Fact]
    public async Task GetReviewAsync_PreservesAvailableSourcesWhenOneSourceFails()
    {
        _identityServiceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                            .ThrowsAsync(new InvalidOperationException("Identity store unavailable."));
        _personaServiceMock.Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
                           .ReturnsAsync([]);
        _conversationServiceMock.Setup(service => service.ListRecordingsAsync(It.IsAny<CancellationToken>()))
                                .ReturnsAsync([]);
        _knowledgeServiceMock.Setup(service => service.GetKnowledge(It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
                             .Returns([
                                 new KnowledgeItemDto { Id = Guid.NewGuid(), Kind = KnowledgeKind.Task, Title = "Review architecture", Status = KnowledgeStatus.Active, CreatedAt = DateTime.UtcNow, LastModifiedAt = DateTime.UtcNow }
                             ]);

        var result = await _service.GetReviewAsync(CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Contains(result.Sources, source => source.SourceKind == MemoryReviewSourceKind.Identity && source.IsAvailable == false);
        Assert.Contains(result.Sources, source => source.SourceKind == MemoryReviewSourceKind.Knowledge && source.IsAvailable);
    }
}
