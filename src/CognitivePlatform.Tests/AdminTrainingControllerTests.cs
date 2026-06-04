using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Controllers.Admin;
using CognitivePlatform.Api.Training;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CognitivePlatform.Tests;

public class AdminTrainingControllerTests
{
    private const string AdminSecret = "test-admin-secret";

    private readonly Mock<IInterpreterTrainingStore> _storeMock = new();
    private readonly AdminTrainingController         _sut;

    public AdminTrainingControllerTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();

        _sut = new AdminTrainingController(config, _storeMock.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Secret"] = AdminSecret;
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private AdminTrainingController CreateUnauthenticated()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();

        var controller = new AdminTrainingController(config, _storeMock.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    // ── GetCorpus ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCorpus_Returns200_WithCountAndRecentRecords()
    {
        var recentRecords = new List<InterpreterTrainingRecord>
                            {
                                new() { UserInput = "add task buy milk", FinalResolvedAction = "AddTask" }
                            };

        _storeMock.Setup(store => store.GetCountAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(1);
        _storeMock.Setup(store => store.GetRecentAsync(10, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(recentRecords);

        var result = await _sut.GetCorpus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetCorpus_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();

        var result = await controller.GetCorpus(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    // ── ExportCorpus ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCorpus_Returns200_AsJsonlFile_WithCorrectShape()
    {
        var record = new InterpreterTrainingRecord
                     {
                         UserInput          = "add task buy milk"
                       , ModelOutput        = """{"actionName":"AddTask"}"""
                       , FinalResolvedAction = "AddTask"
                       , ExecutionSucceeded  = true
                       , LatencyMs           = 150.0
                     };

        _storeMock
            .Setup(store => store.GetForExportAsync(1000, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([record]);

        var result = await _sut.ExportCorpus(1000, false, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/x-ndjson",   file.ContentType);
        Assert.Equal("training-corpus.jsonl",  file.FileDownloadName);

        var lines = Encoding.UTF8.GetString(file.FileContents)
                                 .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);

        var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("add task buy milk",             doc.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("""{"actionName":"AddTask"}""",  doc.RootElement.GetProperty("completion").GetString());
        Assert.Equal("AddTask",                       doc.RootElement.GetProperty("metadata").GetProperty("action").GetString());
        Assert.True(doc.RootElement.GetProperty("metadata").GetProperty("succeeded").GetBoolean());
        Assert.Equal(150.0,                           doc.RootElement.GetProperty("metadata").GetProperty("latency_ms").GetDouble());
    }

    [Fact]
    public async Task ExportCorpus_ClampsLimit_WhenOverMaximum()
    {
        _storeMock
            .Setup(store => store.GetForExportAsync(10000, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.ExportCorpus(99999, false, CancellationToken.None);

        _storeMock.Verify(store => store.GetForExportAsync(10000, false, It.IsAny<CancellationToken>())
                        , Times.Once);
    }

    [Fact]
    public async Task ExportCorpus_ClampsLimit_WhenBelowMinimum()
    {
        _storeMock
            .Setup(store => store.GetForExportAsync(1, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.ExportCorpus(0, false, CancellationToken.None);

        _storeMock.Verify(store => store.GetForExportAsync(1, false, It.IsAny<CancellationToken>())
                        , Times.Once);
    }

    [Fact]
    public async Task ExportCorpus_PassesSucceededOnlyFlag_ToStore()
    {
        _storeMock
            .Setup(store => store.GetForExportAsync(1000, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.ExportCorpus(1000, true, CancellationToken.None);

        _storeMock.Verify(store => store.GetForExportAsync(1000, true, It.IsAny<CancellationToken>())
                        , Times.Once);
    }

    [Fact]
    public async Task ExportCorpus_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();

        var result = await controller.ExportCorpus(ct: CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    // ── GetCorpusStats ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCorpusStats_Returns200_WithStats()
    {
        var stats = new TrainingCorpusStats
                    {
                        TotalRecords   = 42
                      , SucceededCount = 38
                      , SuccessRate    = 0.904
                      , AvgLatencyMs   = 312.5
                    };
        stats.DailyCounts.Add(new DailyRecordCount   { Day = "2026-06-04", RecordCount = 42 });
        stats.ActionCounts.Add(new ActionRecordCount { Action = "AddTask",  RecordCount = 38 });

        _storeMock
            .Setup(store => store.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        var result = await _sut.GetCorpusStats(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(stats, ok.Value);
    }

    [Fact]
    public async Task GetCorpusStats_Returns401_WhenAdminSecretMissing()
    {
        var controller = CreateUnauthenticated();

        var result = await controller.GetCorpusStats(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }
}
