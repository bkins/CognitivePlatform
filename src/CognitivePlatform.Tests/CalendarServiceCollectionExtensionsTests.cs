using CognitivePlatform.Api.Integrations.Calendar;
using CognitivePlatform.Api.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Tests;

public sealed class CalendarServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCalendarServices_RegistersDisconnectedProvider_ForTestingMockRuns()
    {
        var configuration = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["LlmClient:Provider"] = "Mock"
                            })
                            .Build();

        var services = new ServiceCollection();
        services.AddCalendarServices(configuration, "Testing");

        using var provider = services.BuildServiceProvider();

        var calendarProvider = provider.GetRequiredService<ICalendarProvider>();

        Assert.IsType<DisconnectedCalendarProvider>(calendarProvider);
        Assert.False(calendarProvider.IsConnected);
    }
}
