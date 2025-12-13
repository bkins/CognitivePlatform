using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Options;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<IActionRegistry, ActionRegistry>();
builder.Services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();
builder.Services.AddSingleton<IExecutionEngine, ExecutionEngine>();
builder.Services.AddSingleton<ITelemetrySink, ConsoleTelemetrySink>();

builder.Services.AddSingleton<ConversationContextStore>();

// Interpreters
// Mock interpreter (useful for testing)
builder.Services.AddKeyedSingleton<IInterpreter>(KeyedServices.MockInterpreter
                                                , (sp, key) => new MockInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                                  , sp.GetRequiredService<ITelemetrySink>()));

// LLM interpreter (primary)
builder.Services.AddKeyedSingleton<IInterpreter>(KeyedServices.LlmInterpreter
                                                , (sp, key) => new LlmInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                                 , sp.GetRequiredService<ITelemetrySink>()
                                                                                 , sp.GetRequiredService<ILlmClient>()));
builder.Services.AddHttpClient();

builder.Services.Configure<LlmClientSettings>(builder.Configuration.GetSection("LlmClient"));

builder.Services.AddSingleton(resolver =>
{
    var settings   = resolver.GetRequiredService<IOptions<LlmClientSettings>>().Value;
    var httpClient = resolver.GetRequiredService<HttpClient>();
    
    return new OllamaLlmClient(httpClient, settings);
});

builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OllamaLlmClient>());


// Data / persistence - Must be before Domains
BuildDataPersistenceLayer(builder);

// Domains
builder.Services.AddSingleton<JournalService>();


builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

return; // End of Program

// Methods

void BuildDataPersistenceLayer (WebApplicationBuilder webApplicationBuilder)
{

    var dataDirectory = Path.Combine(webApplicationBuilder.Environment.ContentRootPath
                                   , "Data");

    Directory.CreateDirectory(dataDirectory);

    var dbPath = Path.Combine(dataDirectory, "platform.db");

    var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";
    
    webApplicationBuilder.Services.AddSingleton<IObjectStore>(_ => new SqliteObjectStore(connectionString));
}
