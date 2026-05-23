using System.Diagnostics;
using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Insights;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Domains.Feedback;
using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Options;
using System.Text;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Calendar;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Workspace;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal.TestDataGeneration;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Domains.System;
using CognitivePlatform.Api.Integrations.Calendar;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;
using CognitivePlatform.Api.Models.SystemInfo;
using CognitivePlatform.Api.SystemInfo;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.SystemPromptLogging.Models;
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


        var envName = builder.Environment.EnvironmentName;
        builder.Configuration
               .AddJsonFile("appsettings.json")
               .AddJsonFile($"appsettings.{envName}.json", optional: true)
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

        var logStore = new InMemoryLogStore();
        builder.Services.AddSingleton(logStore);
        builder.Logging.AddProvider(new InMemoryLogProvider(logStore));

        builder.Services.Configure<PromptLoggingOptions>(builder.Configuration.GetSection("PromptLogging"));
    builder.Services.Configure<BugReportSettings>(
        builder.Configuration.GetSection("BugReport"));
        builder.Services.AddSingleton<IPromptLogger, PromptLogger>();
        
// Workspace
        builder.Services.AddTransient<WorkspaceActions>();
        builder.Services.AddSingleton<IUserSettingsService, UserSettingsService>();
        builder.Services.AddSingleton<IWorkspaceContext, WorkspaceContext>();

// Core services
        builder.Services.AddSingleton<IAuditLog, ObjectStoreAuditLog>();
        builder.Services.AddSingleton<IActionRegistry, ActionRegistry>();
        builder.Services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();
        builder.Services.AddScoped<IExecutionEngine, ExecutionEngine>();
        
        builder.Services.AddScoped<ITelemetrySink, ConsoleTelemetrySink>();
        builder.Services.AddScoped<TelemetryContext>();
        builder.Services.AddScoped<ITelemetryAggregatorService, TelemetryAggregatorService>();
        
        builder.Services.AddSingleton<ConversationContextStore>();
        builder.Services.AddSingleton<IConversationTurnStore, ConversationTurnStore>();

// Interpreters
        builder.Services
               .AddKeyedScoped<IInterpreter>(KeyedServices.MockInterpreter
                                           , (sp, key) => new MockInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                            , sp.GetRequiredService<ITelemetrySink>()));

        builder.Services.AddScoped<IFastPathResolver, FastPathResolver>();

        // ENH-19 Phase B: rule-based task-complexity classifier. Drives the
        // tier preference the orchestrator forwards to the router.
        builder.Services.AddSingleton<ITaskComplexityClassifier, TaskComplexityClassifier>();

// LLM
        // LLM — named HttpClients, one per provider
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("Ollama");
        builder.Services.AddHttpClient("Groq");
        builder.Services.AddHttpClient("Gemini");
        builder.Services.AddHttpClient("OpenRouter");
        builder.Services.AddHttpClient("Cerebras");
 
        // Settings
        builder.Services.Configure<LlmClientSettings>(builder.Configuration.GetSection("LlmClient"));
        builder.Services.Configure<LlmProviderDefaults>(builder.Configuration.GetSection("Llm:Defaults"));
        builder.Services.AddSingleton<LlmProviderDefaults>(sp => sp.GetRequiredService<IOptions<LlmProviderDefaults>>().Value);

        // ENH-08: bounded turn-history caps for ConversationContext (in-memory, session-scoped).
        // Defaults: MaxTurnHistory = 50, MaxTurnMessageLength = 4096.
        builder.Services.Configure<ConversationContextOptions>(
            builder.Configuration.GetSection("ConversationContext"));

        // Usage tracker — must be registered before LlmClientFactory
        builder.Services.AddSingleton<IGroqUsageTracker, GroqUsageTracker>();

        // EPIC-07: Provider Capacity & Routing — usage aggregator + rate limiter
        builder.Services.AddSingleton<ILlmUsageAggregator, InMemoryLlmUsageAggregator>();
        builder.Services.AddSingleton<ILlmRateLimiter,     InMemoryLlmRateLimiter>();

        // EPIC-07 Phase B: capacity-aware multi-provider router
        builder.Services.AddSingleton<ILlmCapacityRouter>(sp =>
        {
            var configs     = builder.Configuration
                                     .GetSection("LlmModels")
                                     .Get<List<LlmModelConfig>>() ?? [];
            var rateLimiter = sp.GetRequiredService<ILlmRateLimiter>();
            return new LlmCapacityRouter(configs, rateLimiter);
        });

// Factory — selects the active provider at runtime
        builder.Services.AddSingleton<LlmClientFactory>();
        builder.Services.AddSingleton<ILlmClientFactory>(sp => sp.GetRequiredService<LlmClientFactory>());

        // ILlmClient — resolved via factory so swapping providers is a config change
        builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<LlmClientFactory>().Create());

        // ILlmRouter — session-aware dispatcher in front of the factory.
        // Every call re-reads context.Metadata so SetProvider takes effect next turn.
        builder.Services.AddSingleton<ILlmRouter, LlmRouter>();
 
        builder.Services.AddSingleton<LlmModelCatalog>();
        builder.Services.AddSingleton<LlmStartupProbe>();
 
        builder.Services
               .AddKeyedScoped<IInterpreter>(KeyedServices.LlmInterpreter
                                           , (sp, _) => new LlmInterpreter(sp.GetRequiredService<IActionRegistry>()
                                                                         , sp.GetRequiredService<ITelemetrySink>()
                                                                         , sp.GetRequiredService<ILlmRouter>()
                                                                         , sp.GetRequiredService<LlmModelCatalog>()
                                                                         , sp.GetRequiredService<IOptions<LlmClientSettings>>().Value
                                                                         , sp.GetRequiredService<IPromptLogger>()));

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

    //Activity
        builder.Services.AddSingleton<IActivityLog, ObjectStoreActivityLog>();

    //Daily Record
        builder.Services.AddSingleton<IDailyRecordCommandParser, DailyRecordCommandParser>();
        builder.Services.AddSingleton<IDailyRecordService, DailyRecordService>();

    // Knowledge Inbox
        builder.Services.AddSingleton<IKnowledgeService, KnowledgeService>();
        builder.Services.AddSingleton<IKnowledgeSource, JournalKnowledgeSource>();
        builder.Services.AddSingleton<IKnowledgeSource, TaskKnowledgeSource>();
        
    // Identity
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        builder.Services.AddSingleton<IIdentityAnalysisService, IdentityAnalysisService>();

    // System
        builder.Services.AddSingleton<ISystemInfoService, SystemService>();
        builder.Services.Configure<SystemPathsOptions>(builder.Configuration.GetSection("SystemPaths"));
        
    // Insight Engine (Phase C — Task awareness provider added alongside Phase B providers)
        // InsightPolicy is bound from the "Insights" config section so caps tune
        // without a rebuild; missing section falls through to record defaults.
        builder.Services.AddScoped<IInsightProvider, ConversationReflectionInsightProvider>();
        builder.Services.AddScoped<IInsightProvider, JournalActivityInsightProvider>();
        builder.Services.AddScoped<IInsightProvider, TaskAwarenessInsightProvider>();
        builder.Services.AddScoped<IInsightProvider, StressPatternInsightProvider>();
        builder.Services.AddScoped<IInsightProvider, OverdueTasksNoJournalInsightProvider>();
        builder.Services.AddScoped<IInsightEngine, InsightEngine>();
        builder.Services.AddSingleton<IInsightHistoryStore, ObjectStoreInsightHistoryStore>();
        var insightPolicy = builder.Configuration.GetSection("Insights").Get<InsightPolicy>()
                        ?? new InsightPolicy();

        // Enforce the 72-hour repeat window for the Habit category (stress-pattern coaching
        // provider) unless the operator has explicitly configured a different window in appsettings.
        if (!insightPolicy.CategoryRepeatWindows.ContainsKey(InsightCategory.Habit))
            insightPolicy.CategoryRepeatWindows[InsightCategory.Habit] = TimeSpan.FromHours(72);

        builder.Services.AddSingleton<InsightPolicy>(insightPolicy);
        builder.Services.AddScoped<INotificationEngine, NotificationEngine>();

    // Automation Gate (Phase E — empty whitelist by default; opt-in per feature)
        builder.Services.AddSingleton<IAutomationGate>(sp =>
        {
            var logger         = sp.GetRequiredService<ILogger<AutomationGate>>();
            var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return new AutomationGate(allowedActions, logger);
        });

    // Daily Brief
        builder.Services.AddSingleton<IDailyBriefService, DailyBriefService>();

    // Personality
        builder.Services.AddSingleton<IPersonalityService, PersonalityService>();
        builder.Services.AddTransient<PersonalityActions>();
        builder.Services.AddTransient<PersonaEngineActions>();

    // Persona Engine
        builder.Services.AddSingleton<RuleBasedPersonaEngine>();
        builder.Services.AddKeyedSingleton<IIntentAnalyzer>(KeyedServices.RuleBasedIntentAnalyzer
                                                           , (sp, _) => sp.GetRequiredService<RuleBasedPersonaEngine>());

        builder.Services.AddKeyedSingleton<IIntentAnalyzer, KeywordIntentAnalyzer>(
            KeyedServices.KeywordIntentAnalyzer
          , (sp, _) => new KeywordIntentAnalyzer(DefaultKeywordRules.Build()));

        builder.Services.AddKeyedSingleton<IIntentAnalyzer, LlmIntentAnalyzer>(
            KeyedServices.LlmIntentAnalyzer
          , (sp, _) => new LlmIntentAnalyzer(sp.GetRequiredService<ILlmRouter>()));

        builder.Services.AddSingleton<IPersonaEngine, HybridPersonaEngine>();

    // Calendar
        var googleCalendarSection = $"GoogleCalendar:{envName}";
        builder.Services.Configure<GoogleCalendarSettings>(builder.Configuration.GetSection(googleCalendarSection));
        builder.Services.AddHttpClient(googleCalendarSection);
        builder.Services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
        builder.Services.AddTransient<CalendarActions>();

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        
        builder.Services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();

    // Actions
        builder.Services.AddTransient<JournalActions>();
        builder.Services.AddTransient<TaskActions>();
        builder.Services.AddTransient<TaskReasonerActions>();
        builder.Services.AddTransient<InsightsActions>();
        builder.Services.AddTransient<ActivityActions>();
        builder.Services.AddTransient<DebugFastPath>();
        builder.Services.AddTransient<DailyRecordActions>();
        builder.Services.AddTransient<FeedbackActions>();
        builder.Services.AddTransient<SystemActions>();
        builder.Services.AddTransient<IdentityActions>();

// Scalar setup
        builder.Services.AddEndpointsApiExplorer();
        
// Register SystemService
        builder.Services.AddSingleton<SystemService>(sp =>
        {
            var environment = sp.GetRequiredService<IHostEnvironment>();

            // DB lives outside the deploy tree so clean-deploys never touch it.
            // See ADM-05 and BuildDataPersistenceLayer below.
            var dataRoot = Path.Combine(@"C:\CP\Data", environment.EnvironmentName);
            var dbPath   = Path.Combine(dataRoot, "platform.db");

            return new SystemService(environment
                                   , sp.GetRequiredService<IOptions<SystemPathsOptions>>());
            
            // return new SystemService(environment
            //                        , dataRoot
            //                        , dbPath);
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
        
        // Start listening immediately
        var runTask = app.RunAsync(); 
        
// ---------- HEAVY STARTUP  ----------
        SystemEnvironmentInfo envInfo;
        SystemService         sysInfo;
        SystemVersionInfo     verInfo;
        // TODO: `GeminiSettings` need to be generic so that any provider settings could be used
        //GeminiSettings        settings;
        GroqSettings settings;
        bool         googleCalendarIsConnected;
        
        
        using (var scope = app.Services.CreateScope())
        {
            var probe = scope.ServiceProvider.GetRequiredService<LlmStartupProbe>();
            settings = scope.ServiceProvider
                            .GetRequiredService<IOptions<GroqSettings>>()
                            .Value;
            var catalog = scope.ServiceProvider.GetRequiredService<LlmModelCatalog>();
           
            var calendarProvider = scope.ServiceProvider.GetRequiredService<ICalendarProvider>();
            
            await StartProbe(startWithProbeFirst: true
                           , probe
                           , settings
                           , diagnosticsLogger);

            //StoreDefaultModel(settings, catalog);

            sysInfo = scope.ServiceProvider.GetRequiredService<SystemService>();
            envInfo = sysInfo.GetEnvironment();
            verInfo = sysInfo.GetVersion();
            googleCalendarIsConnected = calendarProvider.IsConnected;
        }

        var summary = new StartupSummary
                      {
                              Urls                    = app.Urls.ToList()
                            , EnvInfo                 = envInfo
                            , VerInfo                 = verInfo
                            , SysInfo                 = sysInfo
                            , DefaultModel            = "llama-3.3-70b-versatile"//"gemini-3.1-pro-preview" //"gemini-1.5-Pro" //settings.Model -- TODO: `settings` is not reading from config for some reason, needs investigation. Hardcoding for now to unblock.
                            , Provider                = settings.Provider
                            , GoogleCalendarConnected = googleCalendarIsConnected
                      };

        diagnosticsLogger.LogInformation("{StartupSummary}", summary);
        StartupState.MarkReady();
// ---------- READY ----------

        StartupState.MarkReady();

// Keep server alive
        await runTask;

// ---------- helpers ----------

        void BuildDataPersistenceLayer(WebApplicationBuilder dataBuilder)
        {
            // ADM-05: DB lives at C:\CP\Data\{env}\ — outside the deploy tree so
            // that a clean-wipe of the deploy folder can never destroy production data.
            var dataDirectory = Path.Combine(@"C:\CP\Data", dataBuilder.Environment.EnvironmentName);

            Directory.CreateDirectory(dataDirectory);
//C:\CP\Data\Development
            var dbPath = Path.Combine(dataDirectory, "platform.db");

            var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

            var objectStore = new SqliteObjectStore(connectionString);
            dataBuilder.Services.AddSingleton<IObjectStore>(objectStore);
            dataBuilder.Services.AddSingleton<SqliteObjectStore>(objectStore);
            dataBuilder.Services.AddSingleton<StartupInvariantGuard>();
            
            dataBuilder.Services.AddSingleton<IIdempotencyStore, ObjectStoreIdempotencyStore>();
        }
    }

    private static async Task StartProbe( bool            startWithProbeFirst
                                        , LlmStartupProbe probe
                                        , GroqSettings    settings
                                        , ILogger         log )
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
