using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Configures OpenTelemetry distributed tracing and metrics collection for CognitivePlatform.Api.
/// Exports telemetry using the OpenTelemetry Protocol (OTLP), natively supported by JetBrains Rider,
/// .NET Aspire, and standard OTLP collectors.
/// </summary>
public static class OpenTelemetryServiceCollectionExtensions
{
    public const string ServiceName = "CognitivePlatform.Api";

    public static IServiceCollection AddOpenTelemetryServices(
        this IServiceCollection services
      , IConfiguration configuration
      , IHostEnvironment environment)
    {
        var isEnabled = configuration.GetValue<bool?>("OpenTelemetry:Enabled") ?? true;
        if (!isEnabled)
        {
            return services;
        }

        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: ServiceName
                      , serviceVersion: serviceVersion
                      , serviceInstanceId: Environment.MachineName))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                        })
                        .AddHttpClientInstrumentation(options =>
                        {
                            options.RecordException = true;
                        })
                        .AddOtlpExporter();
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddOtlpExporter();
                });

        return services;
    }
}
