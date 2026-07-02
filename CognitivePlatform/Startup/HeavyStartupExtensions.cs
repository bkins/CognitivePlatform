using System.Diagnostics;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.System;
using CognitivePlatform.Api.Integrations.Calendar;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models.SystemInfo;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Runs the "heavy startup" work that happens after the server is already listening:
/// probing the LLM provider, gathering system/environment info, and logging the
/// consolidated <see cref="StartupSummary"/>. Marks <see cref="StartupState"/> ready
/// when complete.
/// </summary>
public static class HeavyStartupExtensions
{
    public static async Task RunHeavyStartupAsync(this WebApplication app, ILogger diagnosticsLogger)
    {
        using var scope = app.Services.CreateScope();

        var probe = scope.ServiceProvider.GetRequiredService<LlmStartupProbe>();

        var settings = scope.ServiceProvider
                            .GetRequiredService<IOptions<LlmClientSettings>>()
                            .Value;

        var defaults = scope.ServiceProvider.GetRequiredService<LlmProviderDefaults>();
        var provider = settings.Provider;
        var model    = defaults.For(provider) ?? "llama-3.3-70b-versatile";

        var calendarProvider = scope.ServiceProvider.GetRequiredService<ICalendarProvider>();

        await StartProbeAsync(probe, model, diagnosticsLogger);

        var sysInfo = scope.ServiceProvider.GetRequiredService<SystemService>();
        var envInfo = sysInfo.GetEnvironment();
        var verInfo = sysInfo.GetVersion();
        var googleCalendarIsConnected = calendarProvider.IsConnected;

        var summary = new StartupSummary
                      {
                              Urls                    = app.Urls.ToList()
                            , EnvInfo                 = envInfo
                            , VerInfo                 = verInfo
                            , SysInfo                 = sysInfo
                            , DefaultModel            = model
                            , Provider                = provider.ToString()
                            , GoogleCalendarConnected = googleCalendarIsConnected
                      };

        diagnosticsLogger.LogInformation("{StartupSummary}", summary);
        StartupState.MarkReady();
    }

    private static async Task StartProbeAsync(LlmStartupProbe probe, string model, ILogger log)
    {
        var swProbe = new Stopwatch();
        swProbe.Start();

        await probe.RunAsync(model, CancellationToken.None);

        log.LogInformation(probe.ShouldProbeModels
                                   ? $"Ready (Probe completed in {swProbe.Elapsed.Seconds} seconds.)"
                                   : "Probe skipped");
    }
}
