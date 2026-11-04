using System.Diagnostics;
using Codex.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Codex.TestSupport.Telemetry;

// Collects what Codex's ActivitySource and Meter emit into in-memory exporters, two ways:
// - Start(): its own tracer and meter providers on the "Codex" source and meter, with no host (unit tests).
// - AddTo(services) through a host's ConfigureTestServices: the app's own providers get the in-memory exporters too, so
//   a test sees exactly what production would export. The app registers providers only when OTEL_EXPORTER_OTLP_ENDPOINT
//   is set, so set it in the environment before the host starts (a dead address such as http://127.0.0.1:1 is fine).
// The source and meter are process-wide, so a capture also sees what any other host in the process emits: filter by
// what the test itself did, or make the fixture [NonParallelizable]. A capture holds only exported data, never the host;
// dispose it (and the host) at the end of the test or fixture.
public sealed class TelemetryCapture : IDisposable
{
    private TracerProvider? _tracerProvider;
    private MeterProvider? _meterProvider;

    private readonly List<Activity> _activities = [];

    // Snapshots, so a metric reads the same after the provider that exported it is gone.
    private readonly List<MetricSnapshot> _metrics = [];

    public IReadOnlyList<Activity> Activities => _activities;

    public IReadOnlyList<MetricSnapshot> Metrics => _metrics;

    public static TelemetryCapture Start()
    {
        var capture = new TelemetryCapture();
        capture._tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(CodexTelemetry.SourceName)
            .AddInMemoryExporter(capture._activities)
            .Build();
        capture._meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(CodexTelemetry.MeterName)
            .AddInMemoryExporter(capture._metrics)
            .Build();
        return capture;
    }

    // Adds the in-memory exporters to the app's tracer and meter providers. Pass to the host's ConfigureTestServices.
    public void AddTo(IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(_activities));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(_metrics));
    }

    // Exports what has been recorded so far. Ended activities are exported as they end; metrics only on a flush. With
    // AddTo, pass the host's services so its own providers flush.
    public void Flush(IServiceProvider? hostServices = null)
    {
        var tracer = hostServices?.GetService<TracerProvider>() ?? _tracerProvider;
        var meter = hostServices?.GetService<MeterProvider>() ?? _meterProvider;
        tracer?.ForceFlush();
        meter?.ForceFlush();
    }

    public void Dispose()
    {
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
    }
}
