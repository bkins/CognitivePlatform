using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.BrainDump;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Logging;
using Moq;

namespace CognitivePlatform.Tests;

public class BrainDumpServiceTests
{
    private readonly Mock<IObjectStore>              _storeMock    = new();
    private readonly Mock<IWorkspaceContext>          _contextMock  = new();
    private readonly Mock<ILogger<BrainDumpService>> _loggerMock   = new();
    private readonly BrainDumpService                _service;

    public BrainDumpServiceTests()
    {
        _contextMock.Setup(ctx => ctx.ActivePartitionKey).Returns((string?)null);
        _storeMock.Setup(store => store.Save(It.IsAny<BrainDumpSession>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _service = new BrainDumpService(_storeMock.Object
                                      , _contextMock.Object
                                      , _loggerMock.Object);
    }

    // -----------------------------------------------------------------------
    // StartSession
    // -----------------------------------------------------------------------

    [Fact]
    public void StartSession_ReturnsSession_WithNonEmptyId()
    {
        var result = _service.StartSession();

        Assert.NotNull(result);
        Assert.NotEmpty(result.Id);
    }

    [Fact]
    public void StartSession_SetsCreatedAtAndUpdatedAt()
    {
        var before = DateTimeOffset.UtcNow;

        var result = _service.StartSession();

        Assert.True(result.CreatedAt >= before);
        Assert.True(result.UpdatedAt >= before);
    }

    [Fact]
    public void StartSession_IsNotDeleted_AndNotProcessed()
    {
        var result = _service.StartSession();

        Assert.False(result.IsDeleted);
        Assert.False(result.Processed);
    }

    [Fact]
    public void StartSession_SavesSession_ToObjectStore()
    {
        _service.StartSession();

        _storeMock.Verify(store => store.Save(It.IsAny<BrainDumpSession>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Once);
    }

    // -----------------------------------------------------------------------
    // GetSession
    // -----------------------------------------------------------------------

    [Fact]
    public void GetSession_ReturnsNull_WhenNotFound()
    {
        _storeMock.Setup(store => store.Get<BrainDumpSession>(It.IsAny<string>(), It.IsAny<string?>()))
                  .Returns((BrainDumpSession?)null);

        var result = _service.GetSession("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetSession_ReturnsSession_WhenFound()
    {
        var session = new BrainDumpSession { Id = "abc123" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("abc123", null))
                  .Returns(session);

        var result = _service.GetSession("abc123");

        Assert.NotNull(result);
        Assert.Equal("abc123", result.Id);
    }

    // -----------------------------------------------------------------------
    // ListSessions
    // -----------------------------------------------------------------------

    [Fact]
    public void ListSessions_ReturnsOnlyNonDeletedSessions()
    {
        var active  = new BrainDumpSession { IsDeleted = false };
        var deleted = new BrainDumpSession { IsDeleted = true  };

        _storeMock.Setup(store => store.List<BrainDumpSession>(null, null, null))
                  .Returns(new List<BrainDumpSession> { active, deleted });

        var result = _service.ListSessions();

        Assert.Single(result);
        Assert.False(result[0].IsDeleted);
    }

    [Fact]
    public void ListSessions_ReturnsInDescendingCreatedAtOrder()
    {
        var older = new BrainDumpSession { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var newer = new BrainDumpSession { CreatedAt = DateTimeOffset.UtcNow };

        _storeMock.Setup(store => store.List<BrainDumpSession>(null, null, null))
                  .Returns(new List<BrainDumpSession> { older, newer });

        var result = _service.ListSessions();

        Assert.Equal(newer.CreatedAt, result[0].CreatedAt);
        Assert.Equal(older.CreatedAt, result[1].CreatedAt);
    }

    [Fact]
    public void ListSessions_RespectsCountLimit()
    {
        var sessions = Enumerable.Range(1, 15)
                                 .Select(_ => new BrainDumpSession())
                                 .ToList();

        _storeMock.Setup(store => store.List<BrainDumpSession>(null, null, null))
                  .Returns(sessions);

        var result = _service.ListSessions(count: 5);

        Assert.Equal(5, result.Count);
    }

    // -----------------------------------------------------------------------
    // UpdateCategory
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateCategory_ReturnsNull_WhenSessionNotFound()
    {
        _storeMock.Setup(store => store.Get<BrainDumpSession>(It.IsAny<string>(), It.IsAny<string?>()))
                  .Returns((BrainDumpSession?)null);

        var result = _service.UpdateCategory("missing", BrainDumpCategory.Fears, "worried about work");

        Assert.Null(result);
    }

    [Fact]
    public void UpdateCategory_SetsAvoidance_WhenCategoryIsAvoidance()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.Avoidance, "dentist appointment");

        Assert.Equal("dentist appointment", session.Avoidance);
    }

    [Fact]
    public void UpdateCategory_SetsFears_WhenCategoryIsFears()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.Fears, "returning to work");

        Assert.Equal("returning to work", session.Fears);
    }

    [Fact]
    public void UpdateCategory_SetsFrustrations_WhenCategoryIsFrustrations()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.Frustrations, "slow project progress");

        Assert.Equal("slow project progress", session.Frustrations);
    }

    [Fact]
    public void UpdateCategory_SetsDiscouragements_WhenCategoryIsDiscouragements()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.Discouragements, "missed deadline");

        Assert.Equal("missed deadline", session.Discouragements);
    }

    [Fact]
    public void UpdateCategory_SetsGoalsAndBarriers_WhenCategoryIsGoalsAndBarriers()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.GoalsAndBarriers, "finish MAUI app");

        Assert.Equal("finish MAUI app", session.GoalsAndBarriers);
    }

    [Fact]
    public void UpdateCategory_SetsHurtAndSorrow_WhenCategoryIsHurtAndSorrow()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.HurtAndSorrow, "feeling disconnected");

        Assert.Equal("feeling disconnected", session.HurtAndSorrow);
    }

    [Fact]
    public void UpdateCategory_SetsSelfCriticism_WhenCategoryIsSelfCriticism()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.UpdateCategory("s1", BrainDumpCategory.SelfCriticism, "I should be further along");

        Assert.Equal("I should be further along", session.SelfCriticism);
    }

    [Fact]
    public void UpdateCategory_UpdatesUpdatedAt()
    {
        var session = new BrainDumpSession { Id = "s1", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        var before = DateTimeOffset.UtcNow;
        _service.UpdateCategory("s1", BrainDumpCategory.Fears, "test");

        Assert.True(session.UpdatedAt >= before);
    }

    // -----------------------------------------------------------------------
    // MarkProcessed
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkProcessed_ReturnsNull_WhenSessionNotFound()
    {
        _storeMock.Setup(store => store.Get<BrainDumpSession>(It.IsAny<string>(), It.IsAny<string?>()))
                  .Returns((BrainDumpSession?)null);

        var result = _service.MarkProcessed("missing", null, Array.Empty<string>(), Array.Empty<string>());

        Assert.Null(result);
    }

    [Fact]
    public void MarkProcessed_SetsProcessedTrue()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.MarkProcessed("s1", null, Array.Empty<string>(), Array.Empty<string>());

        Assert.True(session.Processed);
    }

    [Fact]
    public void MarkProcessed_SetsProcessedAt()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        var before = DateTimeOffset.UtcNow;
        _service.MarkProcessed("s1", null, Array.Empty<string>(), Array.Empty<string>());

        Assert.NotNull(session.ProcessedAt);
        Assert.True(session.ProcessedAt >= before);
    }

    [Fact]
    public void MarkProcessed_SetsExtractionSummary()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.MarkProcessed("s1", "Top themes: work stress", Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal("Top themes: work stress", session.ExtractionSummary);
    }

    [Fact]
    public void MarkProcessed_AddsExtractedTaskIds()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.MarkProcessed("s1", null, new[] { "task1", "task2" }, Array.Empty<string>());

        Assert.Contains("task1", session.ExtractedTaskIds);
        Assert.Contains("task2", session.ExtractedTaskIds);
    }

    [Fact]
    public void MarkProcessed_AddsExtractedInsightIds()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.MarkProcessed("s1", null, Array.Empty<string>(), new[] { "insight1" });

        Assert.Contains("insight1", session.ExtractedInsightIds);
    }

    // -----------------------------------------------------------------------
    // Delete (soft delete)
    // -----------------------------------------------------------------------

    [Fact]
    public void Delete_SetsIsDeletedTrue()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        _service.Delete("s1");

        Assert.True(session.IsDeleted);
    }

    [Fact]
    public void Delete_SetsDeletedUtc()
    {
        var session = new BrainDumpSession { Id = "s1" };
        _storeMock.Setup(store => store.Get<BrainDumpSession>("s1", null)).Returns(session);

        var before = DateTimeOffset.UtcNow;
        _service.Delete("s1");

        Assert.NotNull(session.DeletedUtc);
        Assert.True(session.DeletedUtc >= before);
    }

    [Fact]
    public void Delete_IsNoOp_WhenSessionNotFound()
    {
        _storeMock.Setup(store => store.Get<BrainDumpSession>(It.IsAny<string>(), It.IsAny<string?>()))
                  .Returns((BrainDumpSession?)null);

        _service.Delete("missing");

        _storeMock.Verify(store => store.Save(It.IsAny<BrainDumpSession>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    [Fact]
    public void GetOrderedSessions_ReturnsSessionsInChronologicalOrder_WithPositions()
    {
        var older = new BrainDumpSession { Id = "older", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new BrainDumpSession { Id = "newer", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };

        _storeMock.Setup(store => store.List<BrainDumpSession>(null, null, null))
                  .Returns(new List<BrainDumpSession> { newer, older });

        var results = _service.GetOrderedSessions();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Position);
        Assert.Equal("older", results[0].Session.Id);
        Assert.Equal(2, results[1].Position);
        Assert.Equal("newer", results[1].Session.Id);
    }

    [Fact]
    public void ResolveByPosition_ReturnsCorrectSession_OrNullIfOutOfRange()
    {
        var older = new BrainDumpSession { Id = "older", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new BrainDumpSession { Id = "newer", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };

        _storeMock.Setup(store => store.List<BrainDumpSession>(null, null, null))
                  .Returns(new List<BrainDumpSession> { newer, older });

        var resolvedFirst = _service.ResolveByPosition(1);
        var resolvedSecond = _service.ResolveByPosition(2);
        var resolvedInvalid = _service.ResolveByPosition(3);

        Assert.Equal("older", resolvedFirst?.Id);
        Assert.Equal("newer", resolvedSecond?.Id);
        Assert.Null(resolvedInvalid);
    }
}
