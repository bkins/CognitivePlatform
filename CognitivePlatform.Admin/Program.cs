using CognitivePlatform.Admin;
using CognitivePlatform.Admin.CpAdminClients;
using CognitivePlatform.Admin.Services;
using CognitivePlatform.Admin.Services.ToolScript;
using CognitivePlatform.Admin.Services.ToolScript.Helpers;
using CognitivePlatform.Admin.Services.ToolScript.Interfaces;
using CP.Shared.Primitives.Avails.Extensions;

using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddScoped<AdminSessionService>();
builder.Services.AddTransient<AdminSecretHandler>();
builder.Services.AddSingleton<EnvironmentService>();
builder.Services.AddTransient<EnvironmentRoutingHandler>();

// Terminal state — singleton, survives page navigation so output persists when user navigates away.
builder.Services.AddSingleton<ITerminalStateService, TerminalStateService>();
builder.Services.AddSingleton<IPowerShellResolver, PowerShellResolver>();

builder.Services.AddTransient<IToolScriptLoader, ToolScriptLoader>();
builder.Services.AddTransient<IToolMetadataReader, ToolMetadataReader>();
builder.Services.AddTransient<IPowerShellParameterReader, PowerShellParameterReader>();
builder.Services.AddTransient<ToolScriptService>();
builder.Services.AddTransient<IToolScriptRunner, ToolScriptRunner>();

// Admin-app error log — singleton ring buffer, visible in the Log Viewer.
// Must be created before the logging provider so both share the same instance.
var adminErrorLog = new AdminErrorLog();
builder.Services.AddSingleton(adminErrorLog);

// Forward every Error/Critical ILogger entry from the Admin app to the ring buffer.
builder.Logging.AddProvider(new AdminErrorLogProvider(adminErrorLog));

var configuration = builder.Configuration;

builder.Services.Configure<ToolScriptOptions>(configuration.GetSection("ToolScripts"));

// All typed clients use this placeholder base address.
// EnvironmentRoutingHandler rewrites it to the currently selected environment URL
// (DEV / QA / PROD) before each request leaves the process.
const string PlaceholderBase = "http://cp-placeholder/";

builder.Services
       .AddHttpClient<IAdminSystemClient, AdminSystemClient>(client => client.BaseAddress = new Uri(PlaceholderBase))
       .AddHttpMessageHandler<AdminSecretHandler>()
       .AddHttpMessageHandler<EnvironmentRoutingHandler>();

builder.Services
       .AddHttpClient<IAdminRegistryClient, AdminRegistryClient>(client => client.BaseAddress = new Uri(PlaceholderBase))
       .AddHttpMessageHandler<AdminSecretHandler>()
       .AddHttpMessageHandler<EnvironmentRoutingHandler>();

builder.Services
       .AddHttpClient<IAdminKnowledgeClient, AdminKnowledgeClient>(client => client.BaseAddress = new Uri(PlaceholderBase))
       .AddHttpMessageHandler<AdminSecretHandler>()
       .AddHttpMessageHandler<EnvironmentRoutingHandler>();

builder.Services
       .AddHttpClient<IAdminJournalClient, AdminJournalClient>(client => client.BaseAddress = new Uri(PlaceholderBase))
       .AddHttpMessageHandler<AdminSecretHandler>()
       .AddHttpMessageHandler<EnvironmentRoutingHandler>();

builder.Services
       .AddHttpClient<IAdminLogsClient, AdminLogsClient>(client => client.BaseAddress = new Uri(PlaceholderBase))
       .AddHttpMessageHandler<AdminSecretHandler>()
       .AddHttpMessageHandler<EnvironmentRoutingHandler>();

builder.Services
       .AddHttpClient<IAdminTrainingClient, AdminTrainingClient>(client => client.BaseAddress = new Uri(PlaceholderBase))
       .AddHttpMessageHandler<AdminSecretHandler>()
       .AddHttpMessageHandler<EnvironmentRoutingHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment().Not())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapGet("/api/backlog-board", () =>
{
    var boardPath = @"C:\Users\benho\source\Application Documentation\UnifiedBacklogBoard.html";
    return File.Exists(boardPath)
        ? Results.File(boardPath, "text/html")
        : Results.NotFound("UnifiedBacklogBoard.html not found.");
});

app.Run();
