using CognitivePlatform.Api.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace CognitivePlatform.Tests;

public class OpenTelemetryServiceCollectionExtensionsTests
{
    private readonly Mock<IHostEnvironment> _hostEnvironmentMock = new();

    public OpenTelemetryServiceCollectionExtensionsTests()
    {
        _hostEnvironmentMock.Setup(environment => environment.EnvironmentName)
                            .Returns("Development");
    }

    [Fact]
    public void AddOpenTelemetryServices_RegistersTelemetryProviders_WhenEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "true"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddOpenTelemetryServices(configuration, _hostEnvironmentMock.Object);
        using var provider = services.BuildServiceProvider();

        var tracerProvider = provider.GetService<TracerProvider>();
        var meterProvider  = provider.GetService<MeterProvider>();

        Assert.NotNull(tracerProvider);
        Assert.NotNull(meterProvider);
    }

    [Fact]
    public void AddOpenTelemetryServices_SkipsRegistration_WhenDisabledInConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "false"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddOpenTelemetryServices(configuration, _hostEnvironmentMock.Object);
        using var provider = services.BuildServiceProvider();

        var tracerProvider = provider.GetService<TracerProvider>();
        var meterProvider  = provider.GetService<MeterProvider>();

        Assert.Null(tracerProvider);
        Assert.Null(meterProvider);
    }

    [Fact]
    public void AddOpenTelemetryServices_DefaultsToEnabled_WhenConfigKeyMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        services.AddOpenTelemetryServices(configuration, _hostEnvironmentMock.Object);
        using var provider = services.BuildServiceProvider();

        var tracerProvider = provider.GetService<TracerProvider>();
        var meterProvider  = provider.GetService<MeterProvider>();

        Assert.NotNull(tracerProvider);
        Assert.NotNull(meterProvider);
    }
}
