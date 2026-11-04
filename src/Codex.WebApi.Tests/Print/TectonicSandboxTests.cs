using Codex.Application.Features.Print.Render;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// The sandboxed Tectonic runner of the real host (needs pandoc, tectonic and codex-sandbox on PATH, as CI installs
// them). Each test writes a tiny .tex into its own folder. A warm bundle cache is not assumed, so nothing here is timed.
[TestFixture]
public class TectonicSandboxTests : ApiFixture
{
    private readonly List<string> _folders = [];

    private ITectonicRunner Runner => Host.Services.GetRequiredService<ITectonicRunner>();

    [OneTimeTearDown]
    public void DeleteFolders()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string JobWith(string body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "codex-render-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        File.WriteAllText(Path.Combine(folder, "main.tex"), "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");
        return folder;
    }

    [Test]
    public async Task Compile_WhenTheDocumentIsPlain_ShouldWriteAPdf()
    {
        var job = JobWith("Hello from Codex.");

        var result = await Runner.CompileAsync(job, "main.tex", CancellationToken.None);

        result.Error.ShouldBeNull(result.Log);
        (await File.ReadAllBytesAsync(result.PdfPath!)).Take(5).ShouldBe("%PDF-"u8.ToArray());
    }

    [Test]
    public async Task Compile_WhenTheDocumentReadsOutsideItsFolder_ShouldFail()
    {
        var job = JobWith("\\input{/etc/hostname}");

        var result = await Runner.CompileAsync(job, "main.tex", CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Log.ShouldContain("Permission denied");
    }

    [Test]
    public async Task Compile_WhenTheDocumentAsksForShellEscape_ShouldNotRunIt()
    {
        var job = JobWith("\\immediate\\write18{touch escaped.txt}Done.");

        var result = await Runner.CompileAsync(job, "main.tex", CancellationToken.None);

        result.Error.ShouldBeNull(result.Log);
        File.Exists(Path.Combine(job, "escaped.txt")).ShouldBeFalse();
        result.Log.ShouldContain("disabled");
    }

    [Test]
    public async Task Compile_WhenTheDocumentWritesIntoTheBundleCache_ShouldNotPlantAFile()
    {
        var cache = Host.Services.GetRequiredService<RenderSettings>().TectonicCache;
        var planted = Path.Combine(cache, "planted-" + Guid.NewGuid().ToString("N") + ".tex");
        var job = JobWith("\\newwrite\\f\\immediate\\openout\\f=" + planted + "\\immediate\\write\\f{x}\\immediate\\closeout\\f Done.");

        await Runner.CompileAsync(job, "main.tex", CancellationToken.None);

        File.Exists(planted).ShouldBeFalse();
    }
}
