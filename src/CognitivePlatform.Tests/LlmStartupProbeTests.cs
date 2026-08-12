using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class LlmStartupProbeTests
{
    private readonly Mock<ILlmClient>               _llmMock     = new();
    private readonly Mock<ILogger<LlmStartupProbe>> _loggerMock  = new();
    private readonly LlmModelCatalog                _catalog     = new();

    [Fact]
    public void Constructor_SetsShouldProbeModelsToTrue_WhenConfigIsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ShouldProbe", "true" }
            })
            .Build();

        var probe = new LlmStartupProbe(_llmMock.Object, _catalog, config, _loggerMock.Object);

        Assert.True(probe.ShouldProbeModels);
    }

    [Fact]
    public void Constructor_SetsShouldProbeModelsToFalse_WhenConfigIsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ShouldProbe", "false" }
            })
            .Build();

        var probe = new LlmStartupProbe(_llmMock.Object, _catalog, config, _loggerMock.Object);

        Assert.False(probe.ShouldProbeModels);
    }

    [Fact]
    public void Constructor_FallbacksToDebugDefault_WhenConfigIsAbsent()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var probe = new LlmStartupProbe(_llmMock.Object, _catalog, config, _loggerMock.Object);

#if DEBUG
        Assert.True(probe.ShouldProbeModels);
#else
        Assert.False(probe.ShouldProbeModels);
#endif
    }

    [Fact]
    public async Task RunAsync_WithJsonError_LogsCleanedMessage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ShouldProbe", "true" }
            })
            .Build();
        var probe = new LlmStartupProbe(_llmMock.Object, _catalog, config, _loggerMock.Object);
        var rawJsonError = """
                           HTTP 429: [{
                             "error": {
                               "code": 429,
                               "message": "You exceeded your current quota. Please retry in 10s."
                             }
                           }]
                           """;

        _llmMock.Setup(llm => llm.ProbeAsync("gemini-3.1-pro-preview", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmModelProbeResult("gemini-3.1-pro-preview", false, Error: rawJsonError));

        await probe.RunAsync("gemini-3.1-pro-preview", CancellationToken.None);

        _loggerMock.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => state != null && state.ToString() != null && state.ToString()!.Contains("HTTP 429: You exceeded your current quota. Please retry in 10s.")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithPlainError_LogsCleanedMessage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ShouldProbe", "true" }
            })
            .Build();
        var probe = new LlmStartupProbe(_llmMock.Object, _catalog, config, _loggerMock.Object);

        _llmMock.Setup(llm => llm.ProbeAsync("gemini-3.1-pro-preview", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmModelProbeResult("gemini-3.1-pro-preview", false, Error: "Connection timed out"));

        await probe.RunAsync("gemini-3.1-pro-preview", CancellationToken.None);

        _loggerMock.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, t) => state != null && state.ToString() != null && state.ToString()!.Contains("Connection timed out")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
