using System.Text;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;

namespace Codex.Application.Features.Print.Export;

// Builds a manuscript copy for a docx, markdown or epub job: the Markdown (ManuscriptMarkdown), then for Word and EPUB
// pandoc converts it inside the print sandbox. A failed conversion throws; the worker records the message on the job.
public interface IManuscriptExporter
{
    Task<BuiltManuscript> BuildAsync(Guid bookId, string settingsJson, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class ManuscriptExporter(IManuscriptMarkdown markdown, SandboxedProcess sandbox, RenderSettings settings, RenderWorkFolders folders) : IManuscriptExporter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // No raw HTML, raw TeX or smart punctuation: the prose is read as written.
    private const string From = "markdown-raw_html-raw_tex-raw_attribute-smart";

    public async Task<BuiltManuscript> BuildAsync(Guid bookId, string settingsJson, CancellationToken cancellationToken)
    {
        var options = JsonSerializer.Deserialize<ManuscriptOptions>(settingsJson, Json)
            ?? throw new InvalidOperationException("The export has no settings.");
        var built = await markdown.BuildAsync(bookId, options, cancellationToken);
        var filename = $"{built.Slug}.{options.Extension}";
        if (options.Format == "markdown")
        {
            return new BuiltManuscript(new UTF8Encoding(false).GetBytes(built.Markdown), options.Mime, filename, built.Notes);
        }

        var bytes = await ConvertAsync(options.Format, built.Markdown, cancellationToken);
        return new BuiltManuscript(bytes, options.Mime, filename, built.Notes);
    }

    private async Task<byte[]> ConvertAsync(string format, string text, CancellationToken cancellationToken)
    {
        var pandoc = RenderSettings.Resolve(settings.Pandoc, RenderSettings.PandocKey);
        var job = folders.Create();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(job, "manuscript.md"), text, new UTF8Encoding(false), cancellationToken);
            var output = format == "docx" ? "manuscript.docx" : "manuscript.epub";
            var args = new List<string> { "--sandbox", "--from", From, "--to", format == "docx" ? "docx" : "epub3", "--wrap=none" };
            if (format == "epub")
            {
                args.Add("--split-level=2");
            }

            args.AddRange(["--output", output, "manuscript.md"]);
            var outcome = await sandbox.RunAsync(
                new SandboxSpec(pandoc, args, job, settings.Timeout / 3, TectonicRunner.MinFileCapBytes), cancellationToken);
            if (outcome.TimedOut)
            {
                throw new InvalidOperationException("Converting the manuscript took too long and was stopped.");
            }

            if (outcome.ExitCode == 125)
            {
                throw new InvalidOperationException("The print sandbox could not start: " + outcome.Stderr.Trim());
            }

            if (outcome.ExitCode != 0)
            {
                throw new InvalidOperationException("Converting the manuscript failed: " + outcome.Stderr.Trim());
            }

            return await File.ReadAllBytesAsync(Path.Combine(job, output), cancellationToken);
        }
        finally
        {
            folders.TryDelete(job);
        }
    }
}
