using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.Feedback;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.SystemPromptLogging.Models;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Logging.Console;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Configures logging: clears default providers, wires the adaptive console formatter,
/// the in-memory log store/provider, and prompt/bug-report logging options.
/// Operates on <see cref="WebApplicationBuilder"/> rather than <see cref="IServiceCollection"/>
/// alone because it also touches <c>builder.Logging</c>.
/// </summary>
public static class LoggingHostBuilderExtensions
{
    public static WebApplicationBuilder ConfigureAdaptiveLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<ConsoleFormatter, AdaptiveConsoleFormatter>();
        builder.Services.Configure<SimpleConsoleFormatterOptions>(options =>
        {
            options.TimestampFormat = "yyyy/MM/dd HH:mm:ss.ff ";
            options.SingleLine      = false; // important for multi-line output
        });

        builder.Logging.AddConsole(options =>
        {
            options.FormatterName = "Adaptive";
        });

        var logStore = new InMemoryLogStore();
        builder.Services.AddSingleton(logStore);
        builder.Logging.AddProvider(new InMemoryLogProvider(logStore));

        // Suppress the built-in Microsoft.Hosting.Lifetime startup/shutdown messages —
        // the adaptive console formatter and StartupSummary log line replace them.
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        return builder;
    }

    public static WebApplicationBuilder ConfigurePromptLogging(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<PromptLoggingOptions>(builder.Configuration.GetSection("PromptLogging"));
        builder.Services.Configure<BugReportSettings>(builder.Configuration.GetSection("BugReport"));
        builder.Services.AddSingleton<IPromptLogger, PromptLogger>();

        return builder;
    }
}
