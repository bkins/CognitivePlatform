using System.Diagnostics;
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
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;
using Scalar.AspNetCore;

public partial class Program
{
    public static async Task Main (string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding  = Encoding.UTF8;

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                                                   {
                                                           Args            = args
                                                         , EnvironmentName = env
                                                   });
        builder.Configuration
               .AddJsonFile("appsettings.json")
               .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);


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
        builder.Services
               .AddKeyedSingleton<IInterpreter>(KeyedServices.MockInterpreter
                                              , (sp
                                               , key) => new MockInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                           , sp.GetRequiredService<ITelemetrySink>()));

        builder.Services.AddSingleton<FastPathResolver>();

// LLM
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient<ILlmClient, OllamaLlmClient>();

        builder.Services.AddSingleton<LlmModelCatalog>();
        builder.Services.AddSingleton<LlmStartupProbe>();

        builder.Services.Configure<LlmClientSettings>(builder.Configuration.GetSection("LlmClient"));

        builder.Services
               .AddKeyedSingleton<IInterpreter>(KeyedServices.LlmInterpreter
                                              , (sp
                                               , _) => new LlmInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                        , sp.GetRequiredService<ITelemetrySink>()
                                                                        , sp.GetRequiredService<ILlmClient>()
                                                                        , sp.GetRequiredService<LlmModelCatalog>()
                                                                        , sp.GetRequiredService<IOptions<LlmClientSettings>>().Value));

// Persistence
        BuildDataPersistenceLayer(builder);

// Domains

//Journals
        builder.Services.AddSingleton<IJournalService, JournalService>();
        builder.Services.AddSingleton<IJournalDraftRepository, InMemoryJournalDraftRepository>();
        builder.Services.AddSingleton<IJournalCommandParser, JournalCommandParser>();

//Journals-Revisions
        builder.Services.AddSingleton<IJournalRevisionRepository, JournalRevisionRepository>();

//Tasks
        builder.Services.AddSingleton<ITaskService, TaskService>();

// Knowledge Inbox
        builder.Services.AddSingleton<IKnowledgeService, KnowledgeService>();
        builder.Services.AddSingleton<IKnowledgeSource, JournalKnowledgeSource>();
        builder.Services.AddSingleton<IKnowledgeSource, TaskKnowledgeSource>();

// Actions
        builder.Services.AddTransient<JournalActions>();
        builder.Services.AddTransient<TaskActions>();
        builder.Services.AddTransient<DebugFastPath>();

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

// Scalar setup
        builder.Services.AddEndpointsApiExplorer();
        
        var app = builder.Build();
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference(options =>
            {
                //http://localhost:5273/scalar
                options.WithTitle("Cognitive Platform API")
                       .WithTheme(ScalarTheme.Mars)
                       .WithDefaultHttpClient(ScalarTarget.CSharp
                                            , ScalarClient.HttpClient);
            });
        }
        
// ---------- HTTP PIPELINE (LISTENING STARTS) ----------

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

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
            var catalog = scope.ServiceProvider.GetRequiredService<LlmModelCatalog>();
            var log     = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            var swProbe = new Stopwatch();
            swProbe.Start();
            await probe.RunAsync(settings.SortedAllowedModels, CancellationToken.None);

            StoreDefaultModel(settings, catalog);

            log.LogInformation("LLM Default Model Selected: {Model}", settings.DefaultModel);
            //swProbe.Stop();
            log.LogInformation($"Ready (Probe completed in {swProbe.Elapsed.Seconds} seconds.)");
        }

// ---------- READY ----------

        StartupState.MarkReady();

// Keep server alive
        await runTask;

// ---------- helpers ----------

        void StoreDefaultModel(LlmClientSettings settings, LlmModelCatalog catalog)
        {
            var ignoreCase = StringComparison.OrdinalIgnoreCase;
    
            var model = settings.SortedAllowedModels
                                .Select(name => catalog.AvailableModels
                                                       .FirstOrDefault(model => model.IsUsable
                                                                             && model.Name
                                                                                     .Equals(name, ignoreCase)))
                                .FirstOrDefault(model => model != null);

            if (model != null)
                settings.DefaultModel = model.Name;
        }

        void BuildDataPersistenceLayer(WebApplicationBuilder builder)
        {
            var dataDirectory = Path.Combine(builder.Environment.ContentRootPath
                                           , "Data"
                                           , builder.Environment.EnvironmentName);

            Directory.CreateDirectory(dataDirectory);

            var dbPath = Path.Combine(dataDirectory, "platform.db");

            var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

            builder.Services.AddSingleton<IObjectStore>( _ => new SqliteObjectStore(connectionString));
        }
    }
}