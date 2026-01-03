using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Options;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding  = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine      = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// Core services
builder.Services.AddSingleton<IActionRegistry, ActionRegistry>();
builder.Services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();
builder.Services.AddSingleton<IExecutionEngine>(sp =>
    new ExecutionEngine(sp.GetRequiredService<ITelemetrySink>(), sp));
builder.Services.AddSingleton<ITelemetrySink, ConsoleTelemetrySink>();
builder.Services.AddSingleton<ConversationContextStore>();

// Interpreters
builder.Services.AddKeyedSingleton<IInterpreter>(KeyedServices.MockInterpreter,
    (sp, key) => new MockInterpreter(
        sp.GetRequiredService<IActionRegistry>(),
        sp.GetRequiredService<ITelemetrySink>()));

builder.Services.AddSingleton<FastPathResolver>();

// LLM
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ILlmClient, OllamaLlmClient>();

builder.Services.AddSingleton<LlmModelCatalog>();
builder.Services.AddSingleton<LlmStartupProbe>();

builder.Services.Configure<LlmClientSettings>(
    builder.Configuration.GetSection("LlmClient"));

builder.Services.AddKeyedSingleton<IInterpreter>(KeyedServices.LlmInterpreter,
    (sp, key) => new LlmInterpreter(
        sp.GetRequiredService<IActionRegistry>(),
        sp.GetRequiredService<ITelemetrySink>(),
        sp.GetRequiredService<ILlmClient>(),
        sp.GetRequiredService<LlmModelCatalog>(),
        sp.GetRequiredService<IOptions<LlmClientSettings>>().Value));

// Persistence
BuildDataPersistenceLayer(builder);

// Domains
builder.Services.AddSingleton<IJournalService, JournalService>();
builder.Services.AddSingleton<ITaskService, TaskService>();

// Actions
builder.Services.AddTransient<JournalActions>();
builder.Services.AddTransient<TaskActions>();
builder.Services.AddTransient<DebugFastPath>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------- HTTP PIPELINE (LISTENING STARTS) ----------

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/ready", () =>
{
    return StartupState.IsReady
        ? Results.Ok("Ready")
        : Results.StatusCode(503);
});

// Start listening immediately
var runTask = app.RunAsync();

// ---------- HEAVY STARTUP (DOES NOT BLOCK LISTENING) ----------

using (var scope = app.Services.CreateScope())
{
    var probe    = scope.ServiceProvider.GetRequiredService<LlmStartupProbe>();
    var settings = scope.ServiceProvider
                        .GetRequiredService<IOptions<LlmClientSettings>>()
                        .Value;
    var catalog  = scope.ServiceProvider.GetRequiredService<LlmModelCatalog>();
    var log      = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await probe.RunAsync(settings.SortedAllowedModels, CancellationToken.None);

    StoreDefaultModel(settings, catalog);

    log.LogInformation("LLM Default Model Selected: {Model}", settings.DefaultModel);
}

// ---------- READY ----------

StartupState.MarkReady();

// Keep server alive
await runTask;

// ---------- helpers ----------

void StoreDefaultModel(LlmClientSettings settings, LlmModelCatalog catalog)
{
    var model = settings.SortedAllowedModels
                        .Select(name => catalog.AvailableModels
                            .FirstOrDefault(m => m.IsUsable &&
                                                 m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault(m => m != null);

    if (model != null)
        settings.DefaultModel = model.Name;
}

void BuildDataPersistenceLayer(WebApplicationBuilder webApplicationBuilder)
{
    var dataDirectory = Path.Combine(webApplicationBuilder.Environment.ContentRootPath, "Data");
    Directory.CreateDirectory(dataDirectory);

    var dbPath = Path.Combine(dataDirectory, "platform.db");
    var connectionString =
        $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

    webApplicationBuilder.Services.AddSingleton<IObjectStore>(
        _ => new SqliteObjectStore(connectionString));
}


// using CognitivePlatform.Api.Actions;
// using CognitivePlatform.Api.Avails;
// using CognitivePlatform.Api.Conversation;
// using CognitivePlatform.Api.Data;
// using CognitivePlatform.Api.Domains.Journal;
// using CognitivePlatform.Api.Domains.Tasks;
// using CognitivePlatform.Api.Registry;
// using CognitivePlatform.Api.Orchestrator;
// using CognitivePlatform.Api.Interpreter;
// using CognitivePlatform.Api.Execution;
// using CognitivePlatform.Api.Telemetry;
// using Microsoft.Extensions.Options;
// using System.Text;
//
// Console.OutputEncoding = Encoding.UTF8;
// Console.InputEncoding  = Encoding.UTF8;
//
// var builder = WebApplication.CreateBuilder(args);
//
// builder.Logging.ClearProviders();
//
// builder.Logging.AddSimpleConsole(options =>
// {
//     options.SingleLine      = true;
//     options.TimestampFormat = "HH:mm:ss ";
// });
//
// // Add services to the container.
// builder.Services.AddSingleton<IActionRegistry, ActionRegistry>();
// builder.Services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();
//
// // builder.Services.AddSingleton<IExecutionEngine, ExecutionEngine>();
// builder.Services.AddSingleton<IExecutionEngine>(sp => new ExecutionEngine(sp.GetRequiredService<ITelemetrySink>()
//                                                                         , sp));
// builder.Services.AddSingleton<ITelemetrySink, ConsoleTelemetrySink>();
//
// builder.Services.AddSingleton<ConversationContextStore>();
//
// // Interpreters
// // Mock interpreter (useful for testing)
// builder.Services.AddKeyedSingleton<IInterpreter>(KeyedServices.MockInterpreter
//                                                 , (sp, key) => new MockInterpreter(sp.GetRequiredService<IActionRegistry>()
//                                                                                   , sp.GetRequiredService<ITelemetrySink>()));
//
// builder.Services.AddSingleton<FastPathResolver>();
//
// // HTTP Client
// builder.Services.AddHttpClient<ILlmClient, OllamaLlmClient>();
// builder.Services.AddHttpClient();
//
// builder.Services.AddSingleton<LlmModelCatalog>();
// builder.Services.AddSingleton<LlmStartupProbe>();
//
// builder.Services.Configure<LlmClientSettings>(builder.Configuration.GetSection("LlmClient"));
//
// builder.Services.AddKeyedSingleton<IInterpreter>(KeyedServices.LlmInterpreter
//                                                , (sp, key) => new LlmInterpreter(sp.GetRequiredService<IActionRegistry>()
//                                                                                , sp.GetRequiredService<ITelemetrySink>()
//                                                                                , sp.GetRequiredService<ILlmClient>()
//                                                                                , sp.GetRequiredService<LlmModelCatalog>()
//                                                                                , sp.GetRequiredService<IOptions<LlmClientSettings>>().Value));
//
// builder.Services.AddSingleton(resolver =>
// {
//     var settings   = resolver.GetRequiredService<IOptions<LlmClientSettings>>().Value;
//     var httpClient = resolver.GetRequiredService<HttpClient>();
//     
//     return new OllamaLlmClient(httpClient, settings);
// });
//
// builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OllamaLlmClient>());
//
// // Data / persistence - Must be before Domains
// BuildDataPersistenceLayer(builder);
//
// // Domains
// builder.Services.AddSingleton<IJournalService, JournalService>();
// builder.Services.AddSingleton<ITaskService, TaskService>();
//
// // Actions 
// builder.Services.AddTransient<JournalActions>();
// builder.Services.AddTransient<TaskActions>();
// builder.Services.AddTransient<DebugFastPath>();
//
// builder.Services.AddControllers();
// builder.Services.AddOpenApi();
//
// var app = builder.Build();
//
// using (var scope = app.Services.CreateScope())
// {
//     var probe    = scope.ServiceProvider.GetRequiredService<LlmStartupProbe>();
//     var settings = scope.ServiceProvider.GetRequiredService<IOptions<LlmClientSettings>>().Value;
//     var catalog  = scope.ServiceProvider.GetRequiredService<LlmModelCatalog>();
//     var log      = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
//     
//     await probe.RunAsync(settings.SortedAllowedModels, CancellationToken.None);
//
//     log.LogInformation("{Buffer}LLM Default Model Selected: {Model}"
//                      , "                                       "
//                      , settings.DefaultModel);
//     
//     StoreDefaultModel(settings, catalog);
// }
//
// if (app.Environment.IsDevelopment())
// {
//     app.MapOpenApi();
// }
//
// app.UseHttpsRedirection();
// app.UseAuthorization();
//
// app.MapControllers();
//
// app.MapGet("/health/ready", () =>
// {
//     return StartupState.IsReady
//                    ? Results.Ok("Ready")
//                    : Results.StatusCode(503);
// });
//
// StartupState.MarkReady();
//
// app.Run();
//
// return; // End of Program
//
// void StoreDefaultModel (LlmClientSettings settings
//                       , LlmModelCatalog   catalog)
// {
//
//     var defaultModel = settings.SortedAllowedModels
//                                .Select(name => catalog.AvailableModels
//                                                       .FirstOrDefault(model => model.IsUsable
//                                                                             && model.Name
//                                                                                     .Equals(name, StringComparison.OrdinalIgnoreCase)))
//                                .FirstOrDefault(model => model != null);
//
//     if (defaultModel != null)
//     {
//         settings.DefaultModel = defaultModel.Name;
//     }
// }
//
// // Methods
//
// void BuildDataPersistenceLayer (WebApplicationBuilder webApplicationBuilder)
// {
//
//     var dataDirectory = Path.Combine(webApplicationBuilder.Environment.ContentRootPath
//                                    , "Data");
//
//     Directory.CreateDirectory(dataDirectory);
//
//     var dbPath = Path.Combine(dataDirectory, "platform.db");
//
//     var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";
//     
//     webApplicationBuilder.Services.AddSingleton<IObjectStore>(_ => new SqliteObjectStore(connectionString));
//
// }
