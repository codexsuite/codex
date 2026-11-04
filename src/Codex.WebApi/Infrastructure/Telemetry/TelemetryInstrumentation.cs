using System.Diagnostics;
using Codex.Contracts;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Codex.WebApi.Infrastructure.Telemetry;

// The built-in instrumentation on the providers TelemetryServices starts. Off unless OTEL_EXPORTER_OTLP_ENDPOINT is set
// (without it there are no providers to configure and nothing here runs).
// Nothing here records a query string, a header, a SQL parameter value or a request body: sign-in credentials, bearer
// tokens, the user's AI provider keys and the manuscript stay out of the telemetry.
public sealed class TelemetryInstrumentation : IServiceModule
{
    private static readonly string[] TracedPrefixes = ["/api", "/mcp"];
    private static readonly string[] UntracedPrefixes = ["/api/health"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        if (!TelemetryResource.IsEnabled(configuration))
        {
            return;
        }

        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing
            .SetSampler(new TraceSampling())
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context => IsTraced(context.Request.Path);
                options.EnrichWithHttpResponse = (activity, response) => UseRouteTemplate(activity, response.HttpContext.GetEndpoint(), response.HttpContext.Request.Method);
                options.EnrichWithException = (activity, _) => activity.SetStatus(ActivityStatusCode.Error);
            })
            .AddNpgsql()
            .AddHttpClientInstrumentation(options =>
            {
                options.EnrichWithHttpRequestMessage = (activity, request) => RedactUrl(activity, request.RequestUri);
                options.EnrichWithHttpResponseMessage = (activity, response) => RedactUrl(activity, response.RequestMessage?.RequestUri);
                options.EnrichWithException = (activity, _) => activity.SetStatus(ActivityStatusCode.Error);
            }));

        // The SignalR hub's meter (Microsoft.AspNetCore.Http.Connections) is framework metrics only: no spans for it.
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics
            .AddMeter(
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Server.Kestrel",
                "Microsoft.AspNetCore.Http.Connections",
                "System.Net.Http",
                "Npgsql")
            .AddRuntimeInstrumentation());
    }

    // The API and the MCP endpoint (its tool calls get their own spans). Not the container probe (/health, /api/health), the
    // SignalR hub (/hubs/book: a negotiate and one long-lived connection per open tab, and its framework metrics cover
    // it) or the SPA's static assets and fallback pages.
    public static bool IsTraced(PathString path)
        => TracedPrefixes.Any(prefix => path.StartsWithSegments(prefix))
           && !UntracedPrefixes.Any(prefix => path.StartsWithSegments(prefix));

    // The route template ("/api/books/{id:guid}"), not the raw path; no query string.
    private static void UseRouteTemplate(Activity activity, Endpoint? endpoint, string method)
    {
        activity.SetTag("url.query", null);
        activity.SetTag("url.path", null);
        if (endpoint is RouteEndpoint { RoutePattern.RawText: { } template })
        {
            var route = template.StartsWith('/') ? template : "/" + template;
            activity.SetTag("http.route", route);
            activity.DisplayName = $"{method} {route}";
        }
    }

    // Scheme, host and path only: no credentials in the authority, no query string (a provider key can ride in either).
    private static void RedactUrl(Activity activity, Uri? uri)
    {
        if (uri is { IsAbsoluteUri: true })
        {
            activity.SetTag("url.full", $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}");
        }
    }
}
