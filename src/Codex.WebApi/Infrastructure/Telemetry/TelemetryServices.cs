using Codex.Application.Telemetry;
using Codex.Contracts;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Codex.WebApi.Infrastructure.Telemetry;

// Exports traces and metrics over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set; otherwise nothing is registered.
// Endpoint, protocol and headers come from the standard OTEL_* settings the exporter reads itself. The pipelines carry
// Codex's own source and meter (CodexTelemetry); TelemetryInstrumentation attaches the built-in instrumentation.
// The endpoint is read here, at registration, so it has to come from the environment (as it does in the box compose).
public sealed class TelemetryServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        if (!TelemetryResource.IsEnabled(configuration))
        {
            return;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => TelemetryResource.Configure(resource, configuration))
            .WithTracing(tracing => tracing.AddSource(CodexTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(CodexTelemetry.MeterName))
            .UseOtlpExporter();
    }
}
