using System;
using System.Threading.Tasks;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class SqliteObjectStorePartitionIsolationTests : IDisposable
{
    private readonly SqliteConnection  _persistentConnection;
    private readonly SqliteObjectStore _store;

    public SqliteObjectStorePartitionIsolationTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _store = new SqliteObjectStore(connectionString);
    }

    public void Dispose() => _persistentConnection.Dispose();

    [Fact]
    public async Task Get_WithNullPartitionKey_DoesNotReturnItemFromDifferentPartition()
    {
        var partitionedItem = new ProcessedRequest
                              {
                                  Id           = "req-work-1"
                                , ResponseJson = "{\"scope\":\"work\"}"
                              };
        await _store.Save(partitionedItem, partitionKey: "work");

        var result = _store.Get<ProcessedRequest>("req-work-1", partitionKey: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithNullPartitionKey_DoesNotReturnItemFromDifferentPartition()
    {
        var partitionedItem = new ProcessedRequest
                              {
                                  Id           = "req-work-async-1"
                                , ResponseJson = "{\"scope\":\"work\"}"
                              };
        await _store.Save(partitionedItem, partitionKey: "work");

        var result = await _store.GetAsync<ProcessedRequest>("req-work-async-1", partitionKey: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task List_WithNullPartitionKey_ReturnsOnlyNullPartitionItems()
    {
        var personalItem = new ProcessedRequest
                           {
                               Id           = "req-personal-1"
                             , ResponseJson = "{\"scope\":\"personal\"}"
                           };
        var workItem     = new ProcessedRequest
                           {
                               Id           = "req-work-2"
                             , ResponseJson = "{\"scope\":\"work\"}"
                           };

        await _store.Save(personalItem, partitionKey: null);
        await _store.Save(workItem,     partitionKey: "work");

        var personalList = _store.List<ProcessedRequest>(partitionKey: null);
        var workList     = _store.List<ProcessedRequest>(partitionKey: "work");

        Assert.Single(personalList);
        Assert.Equal("req-personal-1", personalList[0].Id);

        Assert.Single(workList);
        Assert.Equal("req-work-2", workList[0].Id);
    }

    [Fact]
    public async Task ListAsync_WithNullPartitionKey_ReturnsOnlyNullPartitionItems()
    {
        var personalItem = new ProcessedRequest
                           {
                               Id           = "req-personal-async-1"
                             , ResponseJson = "{\"scope\":\"personal\"}"
                           };
        var workItem     = new ProcessedRequest
                           {
                               Id           = "req-work-async-2"
                             , ResponseJson = "{\"scope\":\"work\"}"
                           };

        await _store.Save(personalItem, partitionKey: null);
        await _store.Save(workItem,     partitionKey: "work");

        var personalList = await _store.ListAsync<ProcessedRequest>(partitionKey: null);
        var workList     = await _store.ListAsync<ProcessedRequest>(partitionKey: "work");

        Assert.Single(personalList);
        Assert.Equal("req-personal-async-1", personalList[0].Id);

        Assert.Single(workList);
        Assert.Equal("req-work-async-2", workList[0].Id);
    }

    [Fact]
    public async Task SoftDelete_WithNullPartitionKey_DoesNotSoftDeleteItemFromDifferentPartition()
    {
        var workItem = new ProcessedRequest
                       {
                           Id           = "req-work-del-1"
                         , ResponseJson = "{\"scope\":\"work\"}"
                       };
        await _store.Save(workItem, partitionKey: "work");

        _store.SoftDelete<ProcessedRequest>("req-work-del-1", partitionKey: null);

        var retrievedWorkItem = _store.Get<ProcessedRequest>("req-work-del-1", partitionKey: "work");
        Assert.NotNull(retrievedWorkItem);
    }

    [Fact]
    public async Task SoftDeleteAsync_WithNullPartitionKey_DoesNotSoftDeleteItemFromDifferentPartition()
    {
        var workItem = new ProcessedRequest
                       {
                           Id           = "req-work-del-async-1"
                         , ResponseJson = "{\"scope\":\"work\"}"
                       };
        await _store.Save(workItem, partitionKey: "work");

        await _store.SoftDeleteAsync<ProcessedRequest>("req-work-del-async-1", partitionKey: null);

        var retrievedWorkItem = await _store.GetAsync<ProcessedRequest>("req-work-del-async-1", partitionKey: "work");
        Assert.NotNull(retrievedWorkItem);
    }

    [Fact]
    public async Task GetDeleted_WithNullPartitionKey_DoesNotReturnItemFromDifferentPartition()
    {
        var workItem = new ProcessedRequest
                       {
                           Id           = "req-work-getdel-1"
                         , ResponseJson = "{\"scope\":\"work\"}"
                       };
        await _store.Save(workItem, partitionKey: "work");

        var result = _store.GetDeleted<ProcessedRequest>("req-work-getdel-1", partitionKey: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task HardDelete_WithNullPartitionKey_DoesNotDeleteRecordFromDifferentPartition()
    {
        var workItem = new ProcessedRequest
                       {
                           Id           = "req-work-harddel-1"
                         , ResponseJson = "{\"scope\":\"work\"}"
                       };
        await _store.Save(workItem, partitionKey: "work");

        var deleted = _store.HardDelete<ProcessedRequest>("req-work-harddel-1", partitionKey: null);

        Assert.False(deleted);
        Assert.NotNull(_store.Get<ProcessedRequest>("req-work-harddel-1", partitionKey: "work"));
    }
}
