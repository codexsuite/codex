using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// ~~struck~~ text goes through the real assembler, each template's Lua filter, pandoc and Tectonic: it has to compile
// around a chapter link, a ^, a mention and a reference footnote (the wrapper's \st is ulem's \sout, not soul's).
// Needs pandoc and tectonic on PATH, as CI installs them.
[TestFixture]
public class StrikethroughPrintTests : ApiFixture
{
    private const string UseId = "0199c0de-0000-7000-8000-000e00000001";
    private const string OtherChapter = "0199c0de-0000-7000-8000-000c00000005";

    private static readonly Guid Book = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid Act = Guid.Parse("00000000-0000-7000-8000-0000000000c2");
    private static readonly Guid Chapter = Guid.Parse("00000000-0000-7000-8000-0000000000c3");
    private static readonly Guid Scene = Guid.Parse("00000000-0000-7000-8000-0000000000c4");

    public static IEnumerable<TestCaseData> Cases()
    {
        var bodies = new Dictionary<string, string>
        {
            ["chapter link"] = $"Start ~~a [Other](chapter:{OtherChapter}) b~~ end.\n",
            ["caret and specials"] = "Then ~~a ^ b and 50% & $x_1 #y~~ done.\n",
            ["mention"] = "Then ~~met [Kent](kb:0199c0de-0000-7000-8000-000b0000000b) here~~ ok.\n",
            ["reference footnote"] = $"Then ~~a [passage](ref:{UseId}) there~~ and ~~a mark[^](ref:{UseId}) x~~.\n",
        };
        return from template in new[] { "manuscript", "novel-6x9", "fantasy-rpg" }
               from body in bodies
               select new TestCaseData(template, body.Value).SetArgDisplayNames(template, body.Key);
    }

    [TestCaseSource(nameof(Cases))]
    public async Task Compile_WhenTextIsStruckThrough_ShouldTypesetIt(string slug, string body)
    {
        var template = Host.Services.GetRequiredService<ITemplateLoader>().GetBuiltIn(slug);
        var options = template.Options.ToDictionary(o => o.Key, o => o.Value.Default);
        var use = Guid.Parse(UseId);
        var snapshot = new ManuscriptSnapshot(
            new RenderBook(Book, "Book", null, "Tester"),
            RenderScope.Chapter,
            [new RenderAct(Act, "Part", null, null)],
            [
                new RenderChapter(Chapter, Act, "Chapter", true, 1, null, null, true),
                new RenderChapter(Guid.Parse(OtherChapter), Act, "Other", true, 2, null, null, true),
            ],
            [new RenderScene(Scene, Chapter, body)],
            new Dictionary<Guid, RenderFigure>(),
            new Dictionary<Guid, RenderImage>(),
            new Dictionary<Guid, RenderEntry>(),
            new Dictionary<Guid, RenderCitation> { [use] = new RenderCitation(use, "[{\"family\":\"Beck\"}]", "Extreme Programming", 2000, "AW", "p. 4") });
        var document = ManuscriptAssembler.Assemble(snapshot, template, template.Trims[0], options, RenderTarget.Pdf);

        using var pdf = await Host.Services.GetRequiredService<IRenderService>().CompileAsync(document, null, CancellationToken.None);

        pdf.Error.ShouldBeNull(pdf.Log);
    }
}
