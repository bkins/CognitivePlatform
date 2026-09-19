using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class InMemoryLoggerTests
{
    [Fact]
    public void IsEnabled_SuppressesEmbeddingHttpInformation_ButKeepsWarnings()
    {
        var logger = new InMemoryLogger("System.Net.Http.HttpClient.OllamaEmbedding.ClientHandler", new InMemoryLogStore());

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }
}
