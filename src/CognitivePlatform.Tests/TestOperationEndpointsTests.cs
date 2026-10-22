using System.Net;
using System.Net.Http.Json;
using CognitivePlatform.Admin.Services;
using CognitivePlatform.Admin.Services.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public sealed class TestOperationEndpointsTests
{
    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong", null, HttpStatusCode.Unauthorized)]
    [InlineData("test-credential", "203.0.113.1", HttpStatusCode.Forbidden)]
    [InlineData("test-credential", null, HttpStatusCode.OK)]
    [InlineData("test-credential", "::1", HttpStatusCode.OK)]
    [InlineData("test-credential", "::ffff:127.0.0.1", HttpStatusCode.OK)]
    public async Task Operation_Endpoints_Enforce_Credential_And_Actual_Loopback(string? credential, string? remote, HttpStatusCode expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-http-" + Guid.NewGuid().ToString("N"));
        await using var app = await Build(root);
        using var http = Client(app);
        if (credential is not null) http.DefaultRequestHeaders.Add("X-Admin-Secret", credential);
        if (remote is not null) http.DefaultRequestHeaders.Add("X-Test-Remote", remote);

        var response = await http.GetAsync("/api/operations/test-profiles");

        Assert.Equal(expected, response.StatusCode);
        await app.StopAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public async Task Backend_Start_And_Status_Are_The_Same_Run_Visible_To_UI_Service()
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-http-" + Guid.NewGuid().ToString("N"));
        await using var app = await Build(root);
        using var http = Client(app);
        http.DefaultRequestHeaders.Add("X-Admin-Secret", "test-credential");

        var response = await http.PostAsJsonAsync("/api/operations/test-runs", new TestRunRequest("unit", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = accepted.GetProperty("id").GetGuid();
        var status = await http.GetAsync($"/api/operations/test-runs/{id}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(id, app.Services.GetRequiredService<TestRunCoordinator>().Latest!.Id);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/operations/test-runs/{id}/results")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/operations/test-runs/{id}/logs?after=-1&limit=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/operations/test-runs/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/operations/test-runs", new { profile = "unit", requestId = Guid.NewGuid(), command = "shell" })).StatusCode);
        var coordinator = app.Services.GetRequiredService<TestRunCoordinator>();
        for (var attempt = 0; attempt < 100 && !coordinator.Get(id)!.FinishedAt.HasValue; attempt++) await Task.Delay(10);
        await app.StopAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public async Task Missing_Server_Credential_Fails_Closed()
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-http-" + Guid.NewGuid().ToString("N"));
        await using var app = await Build(root, "");
        using var http = Client(app);
        http.DefaultRequestHeaders.Add("X-Admin-Secret", "test-credential");

        var response = await http.PostAsJsonAsync("/api/operations/test-runs", new TestRunRequest("unit", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(app.Services.GetRequiredService<TestRunCoordinator>().Latest);
        await app.StopAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static async Task<WebApplication> Build(string root, string secret = "test-credential")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var options = new TestOperationsOptions { RootPath = root, RepositoryRoot = Path.Combine(root, "empty"), Secret = secret };
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<TestProjectCatalog>();
        builder.Services.AddSingleton<TestRunStore>();
        builder.Services.AddSingleton<ITestProcessRunner, TestProcessRunner>();
        builder.Services.AddSingleton<ITerminalStateService, TerminalStateService>();
        builder.Services.AddSingleton<TestRunCoordinator>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Remote", out var remote)) context.Connection.RemoteIpAddress = IPAddress.Parse(remote.ToString());
            await next();
        });
        app.MapTestOperations();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        return new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
    }
}
