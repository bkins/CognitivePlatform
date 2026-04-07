using System.Diagnostics;
using System.Runtime.CompilerServices;
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
using CognitivePlatform.Api.Models.SystemInfo;
using CognitivePlatform.Api.System;
using CognitivePlatform.Api.SystemInfo;
using Microsoft.Extensions.Logging.Console;
using Scalar.AspNetCore;

public partial class Program
{
    public static async Task Main (string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding  = Encoding.UTF8;
        
#if DEBUG
        //Console.SetOut(new InterceptingWriter(Console.Out));
#endif        
        
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                                                   {
                                                           Args            = args
                                                         , EnvironmentName = env
                                                         //, ApplicationName = $"CognitivePlatform.Api.{env}"
                                                   });


        builder.Configuration
               .AddJsonFile("appsettings.json")
               .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
               .AddUserSecrets<Program>(optional: true);
// Set loggers

        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<ConsoleFormatter, AdaptiveConsoleFormatter>();
        builder.Services.Configure<SimpleConsoleFormatterOptions>(options =>
        {
            options.TimestampFormat = "yyyy/MM/dd HH:mm:ss.ff ";
            options.SingleLine      = false; // important for your multi-line output
        });
        
        builder.Logging.AddConsole(options =>
        {
            options.FormatterName = "Adaptive";
        });
        
// Core services
        builder.Services.AddSingleton<IActionRegistry, ActionRegistry>();
        builder.Services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();
        builder.Services.AddScoped<IExecutionEngine, ExecutionEngine>();

        
        builder.Services.AddScoped<ITelemetrySink, ConsoleTelemetrySink>();
        builder.Services.AddScoped<TelemetryContext>();
        
        builder.Services.AddSingleton<ConversationContextStore>();

// Interpreters
        builder.Services
               .AddKeyedScoped<IInterpreter>(KeyedServices.MockInterpreter
                                           , (sp, key) => new MockInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                            , sp.GetRequiredService<ITelemetrySink>()));

        builder.Services.AddScoped<IFastPathResolver, FastPathResolver>();

// LLM
        // LLM — named HttpClients, one per provider
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("Ollama");
        builder.Services.AddHttpClient("Groq");
 
        // Settings
        builder.Services.Configure<LlmClientSettings>(builder.Configuration.GetSection("LlmClient"));
 
        // Usage tracker — must be registered before LlmClientFactory
        builder.Services.AddSingleton<IGroqUsageTracker, GroqUsageTracker>();

// Factory — selects the active provider at runtime
        builder.Services.AddSingleton<LlmClientFactory>();
 
        // ILlmClient — resolved via factory so swapping providers is a config change
        builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<LlmClientFactory>().Create());
 
        builder.Services.AddSingleton<LlmModelCatalog>();
        builder.Services.AddSingleton<LlmStartupProbe>();
 
        builder.Services
               .AddKeyedScoped<IInterpreter>(KeyedServices.LlmInterpreter
                                           , (sp, _) => new LlmInterpreter(sp.GetRequiredService<IActionRegistry>()
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

        // Suppress the built-in messages
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
        
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
        
// ---------- HTTP PIPELINE (LISTENING STARTS) ----------

        app.MapOpenApi();
        
        app.UseAuthorization();

        app.MapControllers();
        
// ---------- Minimal APIs (Health and System info) ----------
// ---------- (These should not relate to business logic) ----        

        app.MapGet("/health/ready"
                 , (ITelemetrySink telemetrySink
                  , bool telemetryOn = false
                  , [CallerMemberName] string caller = "N/A") =>
        {
            if (telemetryOn) telemetrySink.Track($"'/health/ready' Returns Ready or 503 :: Called by: {caller}");
            
            return StartupState.IsReady
                           ? Results.Ok("Ready")
                           : Results.StatusCode(503);
        });

        //app.MapGet("/telemetry/logs", () => ConsoleTelemetrySink.InMemoryTelemetry);

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
        
// ---------- HEAVY STARTUP  ----------
        SystemEnvironmentInfo envInfo;
        SystemService         sysInfo;
        SystemVersionInfo     verInfo;
        GroqSettings          settings;
        
        using (var scope = app.Services.CreateScope())
        {
            var probe    = scope.ServiceProvider.GetRequiredService<LlmStartupProbe>();
            settings = scope.ServiceProvider
                            .GetRequiredService<IOptions<GroqSettings>>()
                            .Value;
            var catalog = scope.ServiceProvider.GetRequiredService<LlmModelCatalog>();
           
            await StartProbe(startWithProbeFirst: true
                           , probe
                           , settings
                           , diagnosticsLogger);

            //StoreDefaultModel(settings, catalog);

            sysInfo = scope.ServiceProvider.GetRequiredService<SystemService>();
            envInfo = sysInfo.GetEnvironment();
            verInfo = sysInfo.GetVersion();
        }

        var summary = new StartupSummary
                      {
                              Urls         = app.Urls.ToList()
                            , EnvInfo      = envInfo
                            , VerInfo      = verInfo
                            , SysInfo      = sysInfo
                            , DefaultModel = settings.Model
                            , Provider     = settings.Provider
                      };

        diagnosticsLogger.LogInformation("{StartupSummary}", summary);
        StartupState.MarkReady();
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

    private static async Task StartProbe( bool              startWithProbeFirst
                                        , LlmStartupProbe   probe
                                        , GroqSettings settings
                                        , ILogger  log )
    {

        if (startWithProbeFirst)
        {
            var swProbe = new Stopwatch();
            swProbe.Start();
                
            await probe.RunAsync(settings.Model
                               , CancellationToken.None);
                
            log.LogInformation(probe.ShouldProbeModels
                                       ? $"Ready (Probe completed in {swProbe.Elapsed.Seconds} seconds.)"
                                       : "Probe skipped");
        }
    }
}