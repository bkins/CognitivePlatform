using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Companion;
using CognitivePlatform.Api.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class CompanionControllerTests
{
    private const string Key = "fixture-only-companion-credential-at-least-32";

    [Fact]
    public async Task Companion_Requires_Dedicated_Credential_And_Revocation_Is_Effective()
    {
        var settings = Settings();
        await using var host = await StartAsync(settings);
        using var client = Client(host);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companion/workspaces")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Document-Analysis-Key", Key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companion/workspaces")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Companion-Key", Key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/companion/workspaces")).StatusCode);
        settings.ClientKeySha256 = new string('A', 64);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companion/workspaces")).StatusCode);
    }

    [Fact]
    public async Task Companion_Reads_Current_Saved_Source_With_No_Mutations_And_Safe_Preview()
    {
        var root = Path.Combine(Path.GetTempPath(), "CP-companion-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "one.md");
        var identity = Guid.NewGuid();
        var source = $"<!-- axiom:documentId={identity:D} -->\n# Café 🌲\n<script>alert('unsafe')</script>\n[unsafe](javascript:alert)\n![remote](https://example.com/private.png)\n";
        await File.WriteAllTextAsync(path, source);
        var settings = Settings(root);
        await using var host = await StartAsync(settings);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Companion-Key", Key);

        using var original = JsonDocument.Parse(await client.GetStringAsync("/api/companion/document?workspaceId=test&path=one.md"));
        var html = original.RootElement.GetProperty("html").GetString()!;
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("private.png", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("axiom:documentId", html);
        Assert.Equal(source, original.RootElement.GetProperty("markdown").GetString());
        Assert.Equal(source, await File.ReadAllTextAsync(path));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/companion/document", new { text = "mutate" })).StatusCode);
        await File.WriteAllTextAsync(path, source + "\nUpdated source");
        using var updated = JsonDocument.Parse(await client.GetStringAsync("/api/companion/document?workspaceId=test&path=one.md"));
        Assert.NotEqual(original.RootElement.GetProperty("contentHash").GetString(), updated.RootElement.GetProperty("contentHash").GetString());
        Assert.Contains("Updated source", updated.RootElement.GetProperty("markdown").GetString());
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("C:/private.md")]
    [InlineData(".git/private.md")]
    [InlineData("secret.txt")]
    public async Task Companion_Rejects_Traversal_And_Unshared_File_Types(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "CP-companion-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var host = await StartAsync(Settings(root));
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Companion-Key", Key);

        var response = await client.GetAsync("/api/companion/document?workspaceId=test&path=" + Uri.EscapeDataString(path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(root, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/companion/files?workspaceId=unshared")).StatusCode);
    }

    [Fact]
    public async Task Companion_Literal_Search_Uses_Saved_Source_And_Shell_Contains_No_Workspace_Data()
    {
        var root = Path.Combine(Path.GetTempPath(), "CP-companion-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "one.md"), "# First\nUnique query token");
        await File.WriteAllTextAsync(Path.Combine(root, "two.md"), "# Second\nOther content");
        await using var host = await StartAsync(Settings(root));
        using var client = Client(host);
        var shell = await client.GetAsync("/companion");
        Assert.True(shell.Headers.Contains("Content-Security-Policy"));
        Assert.DoesNotContain("Unique query token", await shell.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Companion-Key", Key);

        var response = await client.PostAsJsonAsync("/api/companion/search", new CompanionSearchRequest("test", "unique query"));
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result.RootElement.GetProperty("matches").GetArrayLength());
        Assert.Equal("one.md", result.RootElement.GetProperty("matches")[0].GetProperty("path").GetString());
        Assert.Equal("# First\nUnique query token", await File.ReadAllTextAsync(Path.Combine(root, "one.md")));
    }

    [Fact]
    public async Task Companion_Disabled_Or_NonDevelopment_Host_Rejects_Access()
    {
        var settings = Settings();
        settings.Enabled = false;
        await using var disabled = await StartAsync(settings);
        using var disabledClient = Client(disabled);
        disabledClient.DefaultRequestHeaders.Add("X-Companion-Key", Key);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await disabledClient.GetAsync("/api/companion/workspaces")).StatusCode);
        await using var production = await StartAsync(Settings(), "Production");
        using var productionClient = Client(production);
        productionClient.DefaultRequestHeaders.Add("X-Companion-Key", Key);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await productionClient.GetAsync("/api/companion/workspaces")).StatusCode);
    }

    [Fact]
    public async Task Companion_Paginates_Search_And_Rejects_Oversized_Reads()
    {
        var root = Path.Combine(Path.GetTempPath(), "CP-companion-bounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        for (var index = 0; index < 129; index++)
            await File.WriteAllTextAsync(Path.Combine(root, $"{index:D3}.md"), "Café 🌲 needle", Encoding.Unicode);
        await File.WriteAllTextAsync(Path.Combine(root, "oversize.md"), new string('x', 200_001));
        await using var host = await StartAsync(Settings(root));
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Companion-Key", Key);

        using var first = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/companion/search", new CompanionSearchRequest("test", "needle"))).Content.ReadAsStringAsync());
        Assert.Equal(128, first.RootElement.GetProperty("matches").GetArrayLength());
        Assert.Equal(128, first.RootElement.GetProperty("nextOffset").GetInt32());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.GetAsync("/api/companion/document?workspaceId=test&path=oversize.md")).StatusCode);
        using var unicode = JsonDocument.Parse(await client.GetStringAsync("/api/companion/document?workspaceId=test&path=000.md"));
        Assert.Equal("Café 🌲 needle", unicode.RootElement.GetProperty("markdown").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/companion/search", new CompanionSearchRequest("test", "needle", -1))).StatusCode);
    }

    private static CompanionSettings Settings(string? root = null) => new()
    {
        Enabled = true, ClientKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key)))
      , Workspaces = root is null ? [] : [new CompanionWorkspace { Id = "test", Label = "Explicit test workspace", RootPath = root }]
    };

    private static HttpClient Client(WebApplication host)
        => new() { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

    private static async Task<WebApplication> StartAsync(CompanionSettings settings, string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var monitor = new Mock<IOptionsMonitor<CompanionSettings>>();
        monitor.SetupGet(options => options.CurrentValue).Returns(() => settings);
        builder.Services.AddSingleton(monitor.Object);
        builder.Services.AddSingleton<CompanionWorkspaceReader>();
        builder.Services.AddSingleton<CompanionRequestGate>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CompanionController).Assembly);
        var host = builder.Build();
        host.MapControllers();
        await host.StartAsync();
        return host;
    }
}
