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
using CognitivePlatform.Api.Domains.Journal.TestDataGeneration;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;
using CognitivePlatform.Api.System;
using Microsoft.Extensions.Logging.Console;
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
                                                         //, ApplicationName = $"CognitivePlatform.Api.{env}"
                                                   });
      
        
        builder.Configuration
               .AddJsonFile("appsettings.json")
               .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);
// Set loggers

        builder.Logging.ClearProviders();

        builder.Logging.AddConsoleFormatter<AdaptiveConsoleFormatter, SimpleConsoleFormatterOptions>(o =>
        {
            o.TimestampFormat = "HH:mm:ss ";
        });

        builder.Logging.AddConsole(o =>
        {
            o.FormatterName = "Adaptive";
        });

        builder.Logging.AddFilter((provider, category, level) =>
        {
            if (!provider.Contains("Console")) return false;
    
            // Only log Diagnostics at Information and above
            if (category.StartsWith("Diagnostics"))
                return level >= LogLevel.Information;
    
            // Log everything else at your desired level
            return level >= LogLevel.Information;
        });
        
// Core services
        builder.Services.AddSingleton<IActionRegistry, ActionRegistry>();
        builder.Services.AddSingleton<IConversationOrchestrator, ConversationOrchestrator>();
        builder.Services.AddSingleton<IExecutionEngine>(sp => new ExecutionEngine(sp.GetRequiredService<ITelemetrySink>(), sp));
        builder.Services.AddSingleton<ITelemetrySink, ConsoleTelemetrySink>();
        builder.Services.AddSingleton<ConversationContextStore>();

// Interpreters
        builder.Services
               .AddKeyedSingleton<IInterpreter>(KeyedServices.MockInterpreter
                                              , (sp
                                               , key) => new MockInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                           , sp.GetRequiredService<ITelemetrySink>()));

        builder.Services.AddSingleton<IFastPathResolver, FastPathResolver>();

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
        
// Register SystemService
        builder.Services.AddSingleton<SystemService>(sp =>
        {
            var environment = sp.GetRequiredService<IHostEnvironment>();

            // Reuse existing resolved paths
            var dataRoot = Path.Combine(environment.ContentRootPath
                                      , "Data"
                                      , environment.EnvironmentName);
            var dbPath = Path.Combine(dataRoot
                                    , "platform.db");

            return new SystemService(environment
                                   , dataRoot
                                   , dbPath);
        });
        
// Build App
        var app = builder.Build();
        
        using (var scope = app.Services.CreateScope())
        {
            var guard = scope.ServiceProvider.GetRequiredService<StartupInvariantGuard>();
            guard.Enforce();
        }
        
        var diagnosticsLogger = app.Services
                                   .GetRequiredService<ILoggerFactory>()
                                   .CreateLogger("Diagnostics.Program");
        
        app.MapScalarApiReference(options =>
        {
            //http://localhost:5273/scalar
            options.WithTitle($"Cognitive Platform API ({app.Environment.EnvironmentName})")
                   .WithTheme(ScalarTheme.Purple)
                   .WithDefaultHttpClient(ScalarTarget.CSharp
                                        , ScalarClient.HttpClient).Title=$"Cognitive Platform API ({app.Environment.EnvironmentName})";
        });
        
        // if (app.Environment.IsDevelopment())
        // {
        //     
        // }
        
// ---------- HTTP PIPELINE (LISTENING STARTS) ----------

        // if (app.Environment.IsDevelopment())
        // {
        //     
        // }
        app.MapOpenApi();
        
        app.UseAuthorization();

        app.MapControllers();
        
// ---------- Minimal APIs (Health and System info) ----------
// ---------- (These should not relate to business logic) ----        

        app.MapGet("/health/ready", (ITelemetrySink telemetrySink, string caller = "N/A") =>
        {
            telemetrySink.Track("Ready?"
                              , $"Returns Ready or 503 :: Called by: {caller}");
            return StartupState.IsReady
                           ? Results.Ok("Ready")
                           : Results.StatusCode(503);
        });

        app.MapGet("/telemetry/logs", () => ConsoleTelemetrySink.InMemoryTelemetry);

        app.MapGet("/system/environment",
                   (SystemService systemService) =>
                           Results.Ok(systemService.GetEnvironment()));

        app.MapGet("/system/version",
                   (SystemService systemService) =>
                           Results.Ok(systemService.GetVersion()));
        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/debug/generate-journal"
                      , async ( int             paragraphs
                              , int             lineLength
                              , bool            includeUnicode
                              , IJournalService journalService ) =>
                        {
                            var content = JournalStressGenerator.Generate(
                                paragraphs
                              , lineLength
                              , includeUnicode);

                            var entryId = await journalService.AddEntryAsync(content
                                                                           , ["testTag1", "testTag2"]
                                                                           , "Content"
                                                                           , 3
                                                                           , 4
                                                                           , null!);

                            return Results.Ok(new
                                              {
                                                      entryId
                                                    , Length = content.Length
                                              });
                        });
        }

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
            
            var sysInfo = scope.ServiceProvider.GetRequiredService<SystemService>();
            
            var envInfo = sysInfo.GetEnvironment();
            diagnosticsLogger.LogInformation("{SystemEnvironment}", envInfo.ToString());

            var verInfo = sysInfo.GetVersion();
            diagnosticsLogger.LogInformation("{SystemVersion}", verInfo.ToString());
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

        void BuildDataPersistenceLayer(WebApplicationBuilder dataBuilder)
        {
            var dataDirectory = Path.Combine(dataBuilder.Environment.ContentRootPath
                                           , "Data"
                                           , dataBuilder.Environment.EnvironmentName);

            Directory.CreateDirectory(dataDirectory);

            var dbPath = Path.Combine(dataDirectory, "platform.db");

            var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

            dataBuilder.Services.AddSingleton<IObjectStore>( _ => new SqliteObjectStore(connectionString));
            dataBuilder.Services.AddSingleton<StartupInvariantGuard>();
            
            dataBuilder.Services.AddSingleton<IIdempotencyStore, ObjectStoreIdempotencyStore>();
        }
    }
}