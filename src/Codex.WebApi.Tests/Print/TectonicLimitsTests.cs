using Codex.Application.Features.Print.Render;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.WebApi.Tests.Print;

// The runner's limits, on a host whose RenderSettings allow 5 s and a 1 KB PDF: a compile that never ends is killed,
// and an output over the cap is refused.
[TestFixture]
public class TectonicLimitsTests : ApiFixture
{
    private readonly List<string> _folders = [];

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton(sp =>
        RenderSettings.From(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>().ContentRootPath)
            with { Timeout = TimeSpan.FromSeconds(5), MaxPdfBytes = 1024 });

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
    public async Task Compile_WhenItNeverEnds_ShouldBeStoppedAtTheTimeout()
    {
        var job = JobWith("\\def\\forever{\\forever}\\forever");

        var result = await Runner.CompileAsync(job, "main.tex", CancellationToken.None);

        result.Error.ShouldBe("Typesetting took longer than 5 s and was stopped.", result.Log);
        result.PdfPath.ShouldBeNull();
    }

    [Test]
    public async Task Compile_WhenThePdfIsOverTheCap_ShouldFail()
    {
        var job = JobWith(string.Join("\n\n", Enumerable.Repeat("A paragraph that makes the PDF larger than a kilobyte.", 200)));

        var result = await Runner.CompileAsync(job, "main.tex", CancellationToken.None);

        result.Error.ShouldBe("The PDF is larger than the 1 KB limit.", result.Log);
    }
}
