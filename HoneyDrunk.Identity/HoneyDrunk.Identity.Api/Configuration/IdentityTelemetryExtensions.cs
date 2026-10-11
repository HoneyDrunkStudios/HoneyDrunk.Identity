using HoneyDrunk.Telemetry.Abstractions.Conventions;
using HoneyDrunk.Telemetry.OpenTelemetry.Extensions;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace HoneyDrunk.Identity.Api.Configuration;

/// <summary>Preserves HoneyDrunk resources and instrumentation with HTTP signal routing.</summary>
public static class IdentityTelemetryExtensions
{
    /// <summary>Registers telemetry for the public Pulse HTTP receiver.</summary>
    /// <param name="services">Application services.</param>
    /// <param name="configuration">Runtime configuration.</param>
    /// <param name="environment">Host environment name.</param>
    /// <returns>The configured services.</returns>
    public static IServiceCollection AddIdentityTelemetry(
        this IServiceCollection services, IConfiguration configuration, string environment)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? throw new InvalidOperationException("Configure OTEL_EXPORTER_OTLP_ENDPOINT for the deployed telemetry collector.");
        var endpointBuilder = new UriBuilder(new Uri(endpoint, UriKind.Absolute));
        endpointBuilder.Path = endpointBuilder.Path.TrimEnd('/') + '/';
        var baseUri = endpointBuilder.Uri;

        // The pinned HoneyDrunk exporter assigns one literal URL to all signals.
        // Retain its resource conventions; UseOtlpExporter supplies the signal paths.
        services.AddHoneyDrunkOpenTelemetry(options =>
        {
            options.ServiceName = "honeydrunk-identity";
            options.Environment = environment;
            options.EnableTracing = false;
            options.EnableMetrics = false;
            options.EnableLogging = false;
        });
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(
                    TelemetryNames.ActivitySources.PulseCollector,
                    TelemetryNames.ActivitySources.Kernel,
                    TelemetryNames.ActivitySources.Transport,
                    TelemetryNames.ActivitySources.Vault,
                    TelemetryNames.GetActivitySourceName("honeydrunk-identity"),
                    "HoneyDrunk.Data")
                .AddAspNetCoreInstrumentation(options => options.RecordException = true)
                .AddHttpClientInstrumentation(options => options.RecordException = true))
            .WithMetrics(metrics => metrics
                .AddMeter(
                    TelemetryNames.Meters.PulseCollector,
                    TelemetryNames.Meters.Kernel,
                    TelemetryNames.GetMeterName("honeydrunk-identity"))
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation())
            .UseOtlpExporter(OtlpExportProtocol.HttpProtobuf, baseUri);
        return services;
    }
}
