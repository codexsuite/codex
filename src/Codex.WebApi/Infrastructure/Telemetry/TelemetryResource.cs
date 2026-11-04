using System.Reflection;
using OpenTelemetry.Resources;

namespace Codex.WebApi.Infrastructure.Telemetry;

// The resource every signal carries (traces, metrics, and Serilog logs once the OTLP sink is in), and the one switch:
// telemetry is on only when OTEL_EXPORTER_OTLP_ENDPOINT is set. The box sets OTEL_SERVICE_NAME per container (codex,
// codex-worker); host.name and deployment.environment are the collector's to add.
internal static class TelemetryResource
{
    public const string EndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string ServiceNameKey = "OTEL_SERVICE_NAME";
    public const string DefaultServiceName = "codex";

    // One id per process: the machine name says where, the GUID tells a restart apart.
    private static readonly string InstanceId = $"{System.Net.Dns.GetHostName()}-{Guid.NewGuid():N}";

    public static bool IsEnabled(IConfiguration configuration) => !string.IsNullOrWhiteSpace(configuration[EndpointKey]);

    public static ResourceBuilder Configure(ResourceBuilder builder, IConfiguration configuration)
    {
        var name = configuration[ServiceNameKey];
        return builder.AddService(
            serviceName: string.IsNullOrWhiteSpace(name) ? DefaultServiceName : name,
            serviceVersion: Version(),
            serviceInstanceId: InstanceId);
    }

    // The informational version, as /health reports it ("1.0.0+<sha>" in the Docker build).
    public static string Version()
    {
        var assembly = typeof(TelemetryResource).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
