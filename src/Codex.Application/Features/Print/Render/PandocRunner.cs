using Agoda.IoC.Core;

namespace Codex.Application.Features.Print.Render;

// Runs pandoc on a job folder through codex-sandbox (SandboxedProcess): pandoc's own --sandbox (readers and writers
// touch only the files named here), no network at all, reads and writes confined to the job folder, every file it
// writes capped at TectonicRunner.MinFileCapBytes, a third of CODEX_RENDER_TIMEOUT_SECONDS. All paths are relative
// to the job folder. Code blocks are not highlighted (a wrapper needs no highlighting macros). Format is a pandoc writer ("latex", "html5"); Template null writes a fragment.
// From is the reader: "json" (the assembler's AST) unless a template's sample.md is read as "markdown".
public interface IPandocRunner
{
    Task<ProcessOutcome> ConvertAsync(
        string jobDirectory, string inputJson, string format, string? template, IReadOnlyList<string> filters, string output, CancellationToken cancellationToken,
        string from = "json");
}

[RegisterSingleton]
public sealed class PandocRunner(RenderSettings settings, SandboxedProcess sandbox) : IPandocRunner
{
    public async Task<ProcessOutcome> ConvertAsync(
        string jobDirectory, string inputJson, string format, string? template, IReadOnlyList<string> filters, string output, CancellationToken cancellationToken,
        string from = "json")
    {
        var pandoc = RenderSettings.Resolve(settings.Pandoc, RenderSettings.PandocKey);
        var args = new List<string> { "--sandbox", "--from", from, "--to", format, "--top-level-division=part", "--wrap=none", "--syntax-highlighting=none", "--output", output };
        if (template is not null)
        {
            args.Add("--template");
            args.Add(template);
        }
        foreach (var filter in filters)
        {
            args.Add("--lua-filter");
            args.Add(filter);
        }
        args.Add(inputJson);

        return await sandbox.RunAsync(
            new SandboxSpec(pandoc, args, jobDirectory, settings.Timeout / 3, TectonicRunner.MinFileCapBytes),
            cancellationToken);
    }
}
