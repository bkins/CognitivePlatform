using System.Runtime.CompilerServices;
using System.Text.Json;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.System;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Maps the minimal-API endpoints that are infrastructure rather than business logic:
/// readiness probe, system environment/version info, health data push, and client crash reporting.
/// </summary>
public static class MinimalApiEndpointExtensions
{
    public static WebApplication MapSystemEndpoints(this WebApplication app)
    {
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

        app.MapGet("/system/environment",
                   (SystemService systemService) =>
                           Results.Ok(systemService.GetEnvironment()));

        app.MapGet("/system/version",
                   (SystemService systemService) =>
                           Results.Ok(systemService.GetVersion()));

        // -------------------------------------------------------------------
        // POST /health/data — LAA pushes a health snapshot; stored in cache.
        // -------------------------------------------------------------------
        app.MapPost("/health/data", (HealthSnapshot snapshot
                                   , HealthDataCache  cache
                                   , ILogger<HealthDataCache> logger
                                   , IOptions<HealthConnectSettings> settings
                                   , HttpContext httpContext) =>
        {
            var secret = settings.Value.SharedSecret;
            if (secret.HasValue())
            {
                var provided = httpContext.Request.Headers["X-CP-Key"].FirstOrDefault();
                if (provided != secret)
                    return Results.StatusCode(403);
            }

            cache.Store(snapshot);
            logger.LogInformation("Health snapshot received for {Date} from {Platform}: steps={Steps}, sleep={Sleep}m, hr={Hr} bpm"
                                , snapshot.Date
                                , snapshot.Platform
                                , snapshot.Steps
                                , snapshot.SleepMinutes
                                , snapshot.AverageHeartRate);
            return Results.Ok(new { received = true, date = snapshot.Date });
        });

        // -------------------------------------------------------------------
        // GET /health/status — diagnostic: returns the freshest cached snapshot.
        // -------------------------------------------------------------------
        app.MapGet("/health/status", (HealthDataCache cache) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!cache.TryGet(today, out var snapshot))
                return Results.Ok(new { hasData = false, date = today, message = "No snapshot received yet for today." });

            var age = cache.GetAge(today);
            return Results.Ok(new
                              {
                                  hasData          = true
                                , date             = snapshot!.Date
                                , steps            = snapshot.Steps
                                , distanceMetres   = snapshot.DistanceMetres
                                , averageHeartRate = snapshot.AverageHeartRate
                                , sleepMinutes     = snapshot.SleepMinutes
                                , platform         = snapshot.Platform
                                , ageSeconds       = (int)(age?.TotalSeconds ?? 0)
                              });
        });

        // -------------------------------------------------------------------
        // POST /diagnostics/client-crash — LAA forwards unhandled exceptions.
        // -------------------------------------------------------------------
        app.MapPost("/diagnostics/client-crash", async (ClientCrashReport report
                                                       , ILogger<ClientCrashReport> logger
                                                       , IConfiguration config) =>
        {
            logger.LogError("CLIENT CRASH [{Platform}] at {Timestamp}: {Message}\n{StackTrace}"
                          , report.Platform
                          , report.Timestamp
                          , report.Message
                          , report.StackTrace);

            var logDir  = config["CrashLog:Directory"] ?? @"C:\CP\Logs";
            var logPath = Path.Combine(logDir, "crash-log.jsonl");

            try
            {
                Directory.CreateDirectory(logDir);
                var line = JsonSerializer.Serialize(report) + Environment.NewLine;
                await File.AppendAllTextAsync(logPath, line);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not append client crash to {Path}", logPath);
            }

            return Results.Ok(new { received = true });
        });

        return app;
    }

    public static WebApplication MapScalarDocs(this WebApplication app)
    {
        app.MapScalarApiReference(options =>
        {
            //http://localhost:5273/scalar
            options.WithTitle($"Cognitive Platform API ({app.Environment.EnvironmentName})")
                   .WithTheme(ScalarTheme.Purple)
                   .WithDefaultHttpClient(ScalarTarget.CSharp
                                        , ScalarClient.HttpClient).Title = $"Cognitive Platform API ({app.Environment.EnvironmentName})";
        });

        app.MapOpenApi();

        return app;
    }
}

