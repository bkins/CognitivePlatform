using System.Diagnostics;
using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Startup;
using Microsoft.Extensions.Options;
using System.Text;
using CognitivePlatform.Api.SystemInfo;
using Scalar.AspNetCore;
using ConfigurationDiagnostics;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding  = Encoding.UTF8;

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                                                   {
                                                           Args            = args
                                                         , EnvironmentName = env
                                                   });

        var envName = builder.Environment.EnvironmentName;
        var configEnvName = string.Equals(envName, "PROD", StringComparison.OrdinalIgnoreCase) ? "Production" : envName;
        builder.Configuration
               .AddJsonFile("appsettings.json")
               .AddJsonFile($"appsettings.{configEnvName}.json", optional: true)
               .AddUserSecrets<Program>(optional: true)
               .AddEnvironmentVariables();

        builder.ConfigureAdaptiveLogging();
        builder.ConfigurePromptLogging();

// ---------- SERVICE REGISTRATION ----------

        builder.Services.AddWorkspaceServices();
        builder.Services.AddCoreServices();

        builder.Services.AddDataPersistenceLayer(builder.Environment);

        builder.Services.AddJournalServices();
        builder.Services.AddTaskServices();
        builder.Services.AddBrainDumpServices();
        builder.Services.AddMediaServices(builder.Environment);
        builder.Services.AddActivityServices();
        builder.Services.AddDailyRecordServices();
        builder.Services.AddKnowledgeInboxServices();
        builder.Services.AddIdentityServices();

        builder.Services.AddPersonaServices(builder.Configuration);
        builder.Services.AddPersonalityServices();

        builder.Services.AddSystemServices(builder.Configuration);

        builder.Services.AddInsightServices(builder.Configuration);
        builder.Services.AddAutomationGateServices();
        builder.Services.AddDailyBriefServices(builder.Configuration);

        builder.Services.AddHealthServices(builder.Configuration);
        builder.Services.AddWellbeingServices();
        builder.Services.AddMealServices();
        builder.Services.AddSecretsServices();
        builder.Services.AddFileSyncServices(builder.Configuration);
        builder.Services.AddSearchServices();
        builder.Services.AddEmbeddingServices(builder.Configuration);
        builder.Services.AddCalendarServices(builder.Configuration, envName);
        builder.Services.AddCrossAppIntegrations(builder.Configuration);

        builder.Services.AddLlmServices(builder.Configuration);

        builder.Services.AddControllers();

// ---------- BUILD APP ----------

        var app = builder.Build();

#if VERBOSE_STARTUP
        
        var options = new ConfigurationDumpOptions
                      {
                              Mode                = ConfigurationDumpMode.Tree
                            , MaskSensitiveValues = false
                            , SortAlphabetically  = true
                      };

        app.Configuration.DumpToConsole(options);
#endif
        using (var scope = app.Services.CreateScope())
        {
            var guard = scope.ServiceProvider.GetRequiredService<StartupInvariantGuard>();
            guard.Enforce();
        }

        app.SeedActionAndCapabilityRegistries();

        var diagnosticsLogger = app.Services
                                   .GetRequiredService<ILoggerFactory>()
                                   .CreateLogger("Diagnostics.Program");

        app.MapScalarDocs();

// ---------- HTTP PIPELINE (LISTENING STARTS) ----------

        //app.MapOpenApi();
        app.UseAuthorization();
        app.MapControllers();

// ---------- Minimal APIs (Health and System info) ----------
// ---------- (These should not relate to business logic) ----

        app.MapSystemEndpoints();

        // ---------- CRASH LOG — write server-side unhandled exceptions to disk ----------

        var crashLogDir  = builder.Configuration["CrashLog:Directory"] ?? @"C:\CP\Logs";
        var crashLogPath = Path.Combine(crashLogDir, "crash-log.jsonl");

        Directory.CreateDirectory(crashLogDir);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex   = args.ExceptionObject as Exception;
            var line = System.Text.Json.JsonSerializer.Serialize(new
                       {
                           Platform   = "CognitivePlatform.Api"
                         , Message    = ex?.Message    ?? args.ExceptionObject?.ToString() ?? "Unknown"
                         , StackTrace = ex?.StackTrace ?? string.Empty
                         , Source     = "AppDomain.UnhandledException"
                         , Timestamp  = DateTime.UtcNow
                       }) + Environment.NewLine;

            try { File.AppendAllText(crashLogPath, line); }
            catch { /* last-resort: cannot log the logger */ }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            var line = System.Text.Json.JsonSerializer.Serialize(new
                       {
                           Platform   = "CognitivePlatform.Api"
                         , Message    = args.Exception.Message
                         , StackTrace = args.Exception.StackTrace ?? string.Empty
                         , Source     = "TaskScheduler.UnobservedTaskException"
                         , Timestamp  = DateTime.UtcNow
                       }) + Environment.NewLine;

            try { File.AppendAllText(crashLogPath, line); }
            catch { /* last-resort */ }
        };

        // Start listening immediately
        var runTask = app.RunAsync();

// ---------- HEAVY STARTUP ----------

        await app.RunHeavyStartupAsync(diagnosticsLogger);

// ---------- READY ----------

        // Keep server alive
        await runTask;
    }
}
