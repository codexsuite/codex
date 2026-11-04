using Codex.Contracts.Errors;
using Microsoft.Extensions.Configuration;

namespace Codex.Application.Features.Print.Render;

// Where the print toolchain lives and how far a compile may go. Read from configuration when first resolved:
//   CODEX_PANDOC, CODEX_TECTONIC, CODEX_SANDBOX   a path, or a bare name looked up on PATH (default pandoc, tectonic,
//                                                  codex-sandbox). Looked up when a render runs, so a host without the
//                                                  tools starts and answers not_configured only when asked to render.
//   CODEX_TEMPLATES_ROOT                            the built-in template packages (repo `templates/`); relative to the
//                                                  content root; default ../../templates (src/Codex.WebApi in dev and tests)
//   CODEX_RENDER_WORK                               parent of the per-job temp folders (default <temp>/codex-render)
//   CODEX_TECTONIC_CACHE                            Tectonic's bundle cache (default ~/.cache/Tectonic)
//   CODEX_TEMPLATE_CACHE                            template packages unpacked from their stored .zip, one folder per
//                                                  version and package hash (default <temp>/codex-template-cache)
//   CODEX_RENDER_TIMEOUT_SECONDS                    wall clock per Tectonic run (default 300); pandoc gets a third of it
//   CODEX_RENDER_MAX_PDF_MB                         largest PDF a run may write (default 1024: a full book at 300 dpi)
//   CODEX_RENDER_MAX_PROCS                          RLIMIT_NPROC for a run (default 4096). The kernel counts every
//                                                  process and thread of the user, so a shared host needs headroom;
//                                                  the worker container's app user runs little else.
public sealed record RenderSettings(
    string Pandoc,
    string Tectonic,
    string Sandbox,
    string TemplatesRoot,
    string WorkRoot,
    string TectonicCache,
    TimeSpan Timeout,
    long MaxPdfBytes)
{
    public const string PandocKey = "CODEX_PANDOC";
    public const string TectonicKey = "CODEX_TECTONIC";
    public const string SandboxKey = "CODEX_SANDBOX";
    public const string TemplatesRootKey = "CODEX_TEMPLATES_ROOT";
    public const string WorkRootKey = "CODEX_RENDER_WORK";
    public const string TectonicCacheKey = "CODEX_TECTONIC_CACHE";
    public const string TemplateCacheKey = "CODEX_TEMPLATE_CACHE";
    public const string TimeoutKey = "CODEX_RENDER_TIMEOUT_SECONDS";
    public const string MaxPdfKey = "CODEX_RENDER_MAX_PDF_MB";
    public const string MaxProcsKey = "CODEX_RENDER_MAX_PROCS";

    public long MaxProcesses { get; init; } = 4096;

    public string TemplateCache { get; init; } = Path.Combine(Path.GetTempPath(), "codex-template-cache");

    public static RenderSettings From(IConfiguration configuration, string contentRoot)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new RenderSettings(
            Value(configuration, PandocKey) ?? "pandoc",
            Value(configuration, TectonicKey) ?? "tectonic",
            Value(configuration, SandboxKey) ?? "codex-sandbox",
            Path.GetFullPath(Value(configuration, TemplatesRootKey) ?? Path.Combine("..", "..", "templates"), contentRoot),
            Path.GetFullPath(Value(configuration, WorkRootKey) ?? Path.Combine(Path.GetTempPath(), "codex-render"), contentRoot),
            Path.GetFullPath(Value(configuration, TectonicCacheKey) ?? Path.Combine(home, ".cache", "Tectonic"), contentRoot),
            TimeSpan.FromSeconds(Number(configuration, TimeoutKey, 300)),
            Number(configuration, MaxPdfKey, 1024) * 1024 * 1024)
        {
            MaxProcesses = Number(configuration, MaxProcsKey, 4096),
            TemplateCache = Path.GetFullPath(Value(configuration, TemplateCacheKey) ?? Path.Combine(Path.GetTempPath(), "codex-template-cache"), contentRoot),
        };
    }

    // The full path of a tool: as configured when it holds a slash, else the first match on PATH. 503 when missing.
    public static string Resolve(string tool, string key)
    {
        if (tool.Contains('/'))
        {
            return File.Exists(tool) ? Path.GetFullPath(tool) : throw Missing(tool, key);
        }

        var found = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, tool))
            .FirstOrDefault(File.Exists);
        return found ?? throw Missing(tool, key);
    }

    private static ApiException Missing(string tool, string key) =>
        ApiException.NotConfigured($"Print rendering needs {Path.GetFileName(tool)}, which is not installed on this server (set {key}).");

    private static string? Value(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? null : configuration[key];

    private static long Number(IConfiguration configuration, string key, long fallback) =>
        long.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;
}
