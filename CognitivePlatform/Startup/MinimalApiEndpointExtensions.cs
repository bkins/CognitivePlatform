using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.System;
using CognitivePlatform.Api.Telemetry;
using Scalar.AspNetCore;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Maps the minimal-API endpoints that are infrastructure rather than business logic:
/// readiness probe and system environment/version info.
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
