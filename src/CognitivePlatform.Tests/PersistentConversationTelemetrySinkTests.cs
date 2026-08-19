using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class PersistentConversationTelemetrySinkTests : IDisposable
{
    private readonly SqliteConnection                     _connection;
    private readonly SqliteObjectStore                    _objectStore;
    private readonly Mock<ITelemetryStreamService>        _streamMock;
    private readonly PersistentConversationTelemetrySink _sink;

    public PersistentConversationTelemetrySinkTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        _objectStore = new SqliteObjectStore(connectionString);
        _streamMock  = new Mock<ITelemetryStreamService>();

        _sink = new PersistentConversationTelemetrySink(
            new ConsoleTelemetrySink(NullLogger<ConsoleTelemetrySink>.Instance, new TelemetryContext { SessionId = "test-session" })
          , _objectStore
          , _streamMock.Object
          , NullLogger<PersistentConversationTelemetrySink>.Instance);
    }

    [Fact]
    public async Task Track_ConversationCompletedEvent_PersistsRecordAndPublishes()
    {
        var evt = new ConversationCompletedEvent
                  {
                      SessionId   = "sess-1"
                    , TimeElapsed = TimeSpan.FromMilliseconds(450)
                  };

        _sink.Track(evt);
        await Task.Delay(100);

        var records = _objectStore.List<TelemetryRecord>("telemetry");

        Assert.Single(records);
        Assert.Equal("Converse.Ended", records[0].EventName);
        Assert.Equal("sess-1", records[0].SessionId);
        Assert.Equal(450.0, records[0].DurationMs);
        Assert.True(records[0].Success);

        _streamMock.Verify(s => s.Publish(evt), Times.Once);
    }

    [Fact]
    public async Task Track_ExecutionCompletedEvent_PersistsRecordWithActionNameAndSuccess()
    {
        var evt = new ExecutionCompletedEvent
                  {
                      ActionName = "CreateTask"
                    , SessionId  = "sess-2"
                    , Success    = true
                    , Properties = new Dictionary<string, object?> { { "DurationMs", 125.0 } }
                  };

        _sink.Track(evt);
        await Task.Delay(100);

        var records = _objectStore.List<TelemetryRecord>("telemetry");

        Assert.Single(records);
        Assert.Equal("Action.CreateTask", records[0].EventName);
        Assert.Equal(125.0, records[0].DurationMs);
        Assert.True(records[0].Success);
    }

    [Fact]
    public async Task Track_LlmInterpreterErrorEvent_PersistsFailureRecord()
    {
        var evt = new LlmInterpreterErrorEvent
                  {
                      SessionId = "sess-3"
                    , Details   = "Rate limit exceeded"
                  };

        _sink.Track(evt);
        await Task.Delay(100);

        var records = _objectStore.List<TelemetryRecord>("telemetry");

        Assert.Single(records);
        Assert.Equal("Interpreter.Error", records[0].EventName);
        Assert.False(records[0].Success);
    }

    [Fact]
    public async Task Track_IdempotencyHitEvent_PersistsHitRecord()
    {
        var evt = new IdempotencyHitEvent
                  {
                      SessionId = "sess-4"
                  };

        _sink.Track(evt);
        await Task.Delay(100);

        var records = _objectStore.List<TelemetryRecord>("telemetry");

        Assert.Single(records);
        Assert.Equal("Idempotency.Hit", records[0].EventName);
        Assert.True(records[0].Success);
    }

    public void Dispose() => _connection.Dispose();
}
