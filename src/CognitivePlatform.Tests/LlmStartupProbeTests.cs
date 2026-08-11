using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Interpreter;
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
}
