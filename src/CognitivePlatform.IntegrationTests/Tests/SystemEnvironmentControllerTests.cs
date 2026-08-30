using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;

namespace CognitivePlatform.IntegrationTests.Tests;

public sealed class SystemEnvironmentControllerTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetEnvironment_InMemoryFactory_ReportsTestingEnvironment()
    {
        using var fixture = new ApiFixture();

        var response = await fixture.Client.GetAsync("/api/system/environment");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var envName = doc.RootElement
                         .GetProperty("data")
                         .GetProperty("Environment")
                         .GetProperty("environmentName")
                         .GetString();

        Assert.Equal("Testing", envName);
    }
}
