using Codex.Contracts.Errors;
using Agoda.IoC.Core;

namespace Codex.Application.Features.Print.Render;

// Compiles one LaTeX file in its job folder to a PDF with Tectonic, sandboxed (SandboxedProcess):
//   --untrusted (no \write18 or other known-insecure features, even if a template asks) and --only-cached against
//   CODEX_TECTONIC_CACHE mounted read-only, so a run has no network at all and cannot plant files in the shared cache;
//   scripts/warm-tectonic-cache.sh fills that cache beforehand (CI's install step, the worker image build). Reads and
//   writes are confined to the job folder, every file written capped (MinFileCapBytes or CODEX_RENDER_MAX_PDF_MB) and
//   the PDF refused over CODEX_RENDER_MAX_PDF_MB, 4 GB of address space, the wall clock at
//   CODEX_RENDER_TIMEOUT_SECONDS. A PDF over the cap counts as a failure even if Tectonic finished; a file the cache
//   lacks fails the compile with Tectonic's message in the log.
public interface ITectonicRunner
{
    Task<TectonicResult> CompileAsync(string jobDirectory, string texFile, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class TectonicRunner(RenderSettings settings, SandboxedProcess sandbox) : ITectonicRunner
{
    public const long MaxMemoryBytes = 4L * 1024 * 1024 * 1024;

    // Every file Tectonic writes is capped, the bundle cache and format files included, so the cap never drops below
    // this; a PDF between it and CODEX_RENDER_MAX_PDF_MB is refused after the run.
    public const long MinFileCapBytes = 256L * 1024 * 1024;

    public async Task<TectonicResult> CompileAsync(string jobDirectory, string texFile, CancellationToken cancellationToken)
    {
        var tectonic = RenderSettings.Resolve(settings.Tectonic, RenderSettings.TectonicKey);
        if (!Directory.Exists(settings.TectonicCache))
        {
            throw ApiException.NotConfigured(
                $"Print rendering needs a warm Tectonic cache at {RenderSettings.TectonicCacheKey} (scripts/warm-tectonic-cache.sh).");
        }
        var outcome = await sandbox.RunAsync(
            new SandboxSpec(
                tectonic,
                ["--untrusted", "--only-cached", "--keep-logs", "--chatter", "minimal", "--color", "never", "--outdir", jobDirectory, texFile],
                jobDirectory,
                settings.Timeout,
                Math.Max(settings.MaxPdfBytes, MinFileCapBytes),
                MaxMemoryBytes,
                Variables: new Dictionary<string, string> { ["TECTONIC_CACHE_DIR"] = settings.TectonicCache },
                ReadOnlyDirectories: [settings.TectonicCache]),
            cancellationToken);

        var pdf = Path.Combine(jobDirectory, Path.ChangeExtension(texFile, ".pdf"));
        var logFile = Path.Combine(jobDirectory, Path.ChangeExtension(texFile, ".log"));
        var texLog = File.Exists(logFile) ? await ReadTailAsync(logFile, cancellationToken) : string.Empty;
        var log = string.Join("\n", new[] { outcome.Stderr, outcome.Stdout, texLog }.Where(s => s.Length > 0));

        string? error = null;
        if (outcome.TimedOut)
        {
            error = $"Typesetting took longer than {settings.Timeout.TotalSeconds:0} s and was stopped.";
        }
        else if (outcome.ExitCode == 125)
        {
            error = "The print sandbox could not start: " + outcome.Stderr.Trim();
        }
        else if (outcome.ExitCode != 0 || !File.Exists(pdf))
        {
            error = File.Exists(pdf) && new FileInfo(pdf).Length >= settings.MaxPdfBytes
                ? TooLarge()
                : "Typesetting failed; see the log.";
        }
        else if (new FileInfo(pdf).Length > settings.MaxPdfBytes)
        {
            error = TooLarge();
        }

        return new TectonicResult(error is null ? pdf : null, log, error, outcome.Duration);
    }

    private string TooLarge() => settings.MaxPdfBytes >= 1024 * 1024
        ? $"The PDF is larger than the {settings.MaxPdfBytes / (1024 * 1024)} MB limit."
        : $"The PDF is larger than the {settings.MaxPdfBytes / 1024} KB limit.";

    private static async Task<string> ReadTailAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return text.Length > SandboxedProcess.MaxLogChars ? text[^SandboxedProcess.MaxLogChars..] : text;
    }
}
