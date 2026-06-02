using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;

public class ObjectStoreActivityLogTests
{
    private readonly Mock<IObjectStore>      _storeMock            = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();
    private readonly ObjectStoreActivityLog  _log;

    public ObjectStoreActivityLogTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<ActivityEvent>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("id");

        _workspaceContextMock.Setup(ctx => ctx.ActivePartitionKey)
                             .Returns((string?)null); // personal workspace

        _log = new ObjectStoreActivityLog(_storeMock.Object, _workspaceContextMock.Object);
    }

    [Fact]
    public async Task LogAsync_CallsStoreSave_WithEventAndId()
    {
        var activityEvent = new ActivityEvent
                            {
                                    ActivityType = "run"
                                  , Duration     = 30
                                  , Unit         = "minutes"
                            };

        await _log.LogAsync(activityEvent);

        _storeMock.Verify(store => store.Save(activityEvent, null, activityEvent.Id), Times.Once);
    }

    [Fact]
    public async Task LogAsync_Throws_WhenTokenIsCancelled()
    {
        var activityEvent = new ActivityEvent { ActivityType = "run" };
        var cts           = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _log.LogAsync(activityEvent, cts.Token));
    }

    [Fact]
    public async Task ListAsync_ReturnsEvents_InDescendingChronologicalOrder()
    {
        var older = new ActivityEvent { OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-10), ActivityType = "read" };
        var newer = new ActivityEvent { OccurredUtc = DateTimeOffset.UtcNow,                ActivityType = "run"  };

        _storeMock.Setup(store => store.List<ActivityEvent>(null, null, null))
                  .Returns(new List<ActivityEvent> { older, newer });

        var result = await _log.ListAsync();

        Assert.Equal(2,       result.Count);
        Assert.Equal(newer.Id, result[0].Id);
        Assert.Equal(older.Id, result[1].Id);
    }

    [Fact]
    public async Task ListAsync_PassesDateBounds_ToStore()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to   = DateTimeOffset.UtcNow;

        _storeMock.Setup(store => store.List<ActivityEvent>(null, from, to))
                  .Returns(new List<ActivityEvent>());

        await _log.ListAsync(fromUtc: from, toUtc: to);

        _storeMock.Verify(store => store.List<ActivityEvent>(null, from, to), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenNoEventsExist()
    {
        _storeMock.Setup(store => store.List<ActivityEvent>(null, null, null))
                  .Returns(new List<ActivityEvent>());

        var result = await _log.ListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task LogAndList_RoundTrip_ViaInMemoryStore()
    {
        var inMemoryStore      = new FakeObjectStore();
        var workspaceCtxMock   = new Mock<IWorkspaceContext>();
        workspaceCtxMock.Setup(ctx => ctx.ActivePartitionKey).Returns((string?)null);
        var log = new ObjectStoreActivityLog(inMemoryStore, workspaceCtxMock.Object);

        var first  = new ActivityEvent { ActivityType = "run",        OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var second = new ActivityEvent { ActivityType = "meditation", OccurredUtc = DateTimeOffset.UtcNow                };

        await log.LogAsync(first);
        await log.LogAsync(second);

        var result = await log.ListAsync();

        Assert.Equal(2,                 result.Count);
        Assert.Equal("meditation",      result[0].ActivityType);
        Assert.Equal("run",             result[1].ActivityType);
    }

    [Fact]
    public async Task ListAsync_Throws_WhenTokenIsCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _log.ListAsync(null, null, cts.Token));
    }

    /// <summary>Minimal in-memory IObjectStore stub for round-trip testing.</summary>
    private sealed class FakeObjectStore : IObjectStore
    {
        private readonly Dictionary<string, object> _objects = new();

        public Task<string> Save<T>(T value, string? partitionKey = null, string? id = null)
        {
            var key = id ?? Guid.NewGuid().ToString("N");
            _objects[key] = value!;
            return Task.FromResult(key);
        }

        public T? Get<T>(string id, string? partitionKey = null)
            => _objects.TryGetValue(id, out var existing) ? (T)existing : default;

        public T? GetDeleted<T>(string id, string? partitionKey = null) => default;

        public Task<T?> GetAsync<T>( string            id
                                   , string?           partitionKey      = null
                                   , CancellationToken cancellationToken = default )
            => Task.FromResult(Get<T>(id, partitionKey));

        public IReadOnlyList<T> List<T>(string?         partitionKey = null
                                      , DateTimeOffset? fromUtc      = null
                                      , DateTimeOffset? toUtc        = null)
            => _objects.Values.OfType<T>().ToList();

        public Task<IReadOnlyList<T>> ListAsync<T>( string?           partitionKey      = null
                                                  , DateTimeOffset?   fromUtc           = null
                                                  , DateTimeOffset?   toUtc             = null
                                                  , CancellationToken cancellationToken = default )
            => Task.FromResult(List<T>(partitionKey, fromUtc, toUtc));

        public bool SoftDelete<T>(string id, string? partitionKey = null) => _objects.Remove(id);

        public Task<bool> SoftDeleteAsync<T>( string            id
                                            , string?           partitionKey      = null
                                            , CancellationToken cancellationToken = default )
            => Task.FromResult(SoftDelete<T>(id, partitionKey));
    }
}
