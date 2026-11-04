using Microsoft.Extensions.Configuration;

namespace Codex.Application.Features.Print.Render;

// Which half of the app a process is (CODEX_ROLE): "web" (the default: serves the SPA and the API, claims no
// render jobs, since the web image has no print toolchain), "worker" (the worker image: claims render and export
// jobs; the web-only hosted services are not started, see Codex.WebApi/Worker/WorkerRoleServices) or "all" (one
// process does both: local dev and tests that run jobs). Job runners check RunsJobs before claiming rows.
public static class CodexRole
{
    public const string Key = "CODEX_ROLE";
    public const string Web = "web";
    public const string Worker = "worker";
    public const string All = "all";

    public static string Of(IConfiguration configuration) => configuration[Key]?.Trim().ToLowerInvariant() switch
    {
        Worker => Worker,
        All => All,
        _ => Web,
    };

    public static bool RunsJobs(IConfiguration configuration) => Of(configuration) is Worker or All;

    public static bool ServesWeb(IConfiguration configuration) => Of(configuration) is Web or All;
}
