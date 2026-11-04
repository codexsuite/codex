using System.Reflection;
using Codex.Contracts;

namespace Codex.WebApi.Infrastructure;

// /health is what the container HEALTHCHECK calls; /api/health is the same payload under /api.
// The Docker build stamps the commit into the informational version (-p:SourceRevisionId=...,
// "1.0.0+<sha>"), so the response names the running build. Both answer without a session.
public sealed class HealthEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var version = ApiMarker.Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok", version })).AllowAnonymous();
        endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok", version })).AllowAnonymous();
    }
}
