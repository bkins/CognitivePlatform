using Moq;
using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Tests;

public class ObjectStoreAuditLogTests
{
    private readonly Mock<IObjectStore>  _storeMock = new();
    private readonly ObjectStoreAuditLog _log;

    public ObjectStoreAuditLogTests()
    {
        _storeMock.Setup(s => s.Save(It.IsAny<AuditEvent>()
                                   , It.IsAny<string?>()
                                   , It.IsAny<string?>()))
                  .ReturnsAsync("id");

        _log = new ObjectStoreAuditLog(_storeMock.Object);
    }

    // ================================================================
    // APPEND ASYNC
    // ================================================================

    [Fact]
    public async Task AppendAsync_CallsStoreSave_WithEventAndId()
    {
        var evt = new AuditEvent
                  {
                          ActionName = "TestAction"
                        , Outcome    = AuditOutcome.Success
                  };

        await _log.AppendAsync(evt);

        _storeMock.Verify(s => s.Save(evt, null, evt.Id), Times.Once);
    }

    [Fact]
    public async Task AppendAsync_DoesNotThrow_WhenStoreSucceeds()
    {
        var evt = new AuditEvent { ActionName = "TestAction", Outcome = AuditOutcome.Failure };

        var exception = await Record.ExceptionAsync(() => _log.AppendAsync(evt));

        Assert.Null(exception);
    }

    // ================================================================
    // LIST
    // ================================================================

    [Fact]
    public void List_ReturnsEvents_InDescendingChronologicalOrder()
    {
        var older = new AuditEvent { OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new AuditEvent { OccurredUtc = DateTimeOffset.UtcNow };

        _storeMock.Setup(s => s.List<AuditEvent>(null, null, null))
                  .Returns(new List<AuditEvent> { older, newer });

        var result = _log.List();

        Assert.Equal(2, result.Count);
        Assert.Equal(newer.Id, result[0].Id);
        Assert.Equal(older.Id, result[1].Id);
    }

    [Fact]
    public void List_PassesDateBounds_ToStore()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to   = DateTimeOffset.UtcNow;

        _storeMock.Setup(store => store.List<AuditEvent>(null, from, to))
                  .Returns(new List<AuditEvent>());

        _log.List(fromUtc: from, toUtc: to);

        _storeMock.Verify(store => store.List<AuditEvent>(null, from, to), Times.Once);
    }

    [Fact]
    public void List_ReturnsEmpty_WhenNoEventsExist()
    {
        _storeMock.Setup(store => store.List<AuditEvent>(null, null, null))
                  .Returns(new List<AuditEvent>());

        var result = _log.List();

        Assert.Empty(result);
    }
}
