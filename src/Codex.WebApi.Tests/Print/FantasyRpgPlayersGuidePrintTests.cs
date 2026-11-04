using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts.Storage;
using Codex.TestSupport.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// #13 phase-2 acceptance, the Players Guide's pages next to Codex's: what the comparison found in the Fantasy RPG
// template, on a book shaped like the imported guide (a part with opener art, a chapter whose first paragraph follows a
// subheading, a spell, hard-wrapped lines). The whole book compiles through the real assembler, the template's Lua
// filter, pandoc and Tectonic; the tests read its LaTeX. Needs pandoc and tectonic on PATH, as CI installs them.
[TestFixture]
public class FantasyRpgPlayersGuidePrintTests : ApiFixture
{
    private static readonly Guid Book = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid Act = Guid.Parse("00000000-0000-7000-8000-0000000000c2");
    private static readonly Guid Chapter = Guid.Parse("00000000-0000-7000-8000-0000000000c3");
    private static readonly Guid Scene = Guid.Parse("00000000-0000-7000-8000-0000000000c4");
    private static readonly Guid Spell = Guid.Parse("00000000-0000-7000-8000-0000000000c5");
    private static readonly Guid Art = Guid.Parse("00000000-0000-7000-8000-0000000000c6");

    private static readonly string Body = $$"""
        # Overview

        Fighters in Velmara train from an early age.

        **Porters' Rebellion:** the "free" folk's war -- it's *"over"*, they say...

        :::sidebar{title="The Porters' Oath"}
        Say it "twice".
        :::

        U: unlimited.
        *: Dwarfs have no limit on a long march

        ::spell{kb="{{Spell}}"}

        """;

    private const string SpellFields =
        """{"level-school":"4th-level alteration","casting-time":"1 round","range":"Touch","components":"V, S, M (a small piece of stone)","duration":"1 turn/level"}""";

    private Task<(RenderedPdf Pdf, string Tex)>? _render;

    private Task<(RenderedPdf Pdf, string Tex)> RenderAsync() => _render ??= CompileAsync();

    private async Task<(RenderedPdf Pdf, string Tex)> CompileAsync()
    {
        var key = $"test/{Art}.png";
        await using (var png = new MemoryStream(TestImages.Photo(64, 64)))
        {
            await Host.Services.GetRequiredService<IBlobStore>().PutAsync(key, png, "image/png", CancellationToken.None);
        }

        var template = Host.Services.GetRequiredService<ITemplateLoader>().GetBuiltIn("fantasy-rpg");
        var options = template.Options.ToDictionary(o => o.Key, o => o.Value.Default);
        var snapshot = new ManuscriptSnapshot(
            new RenderBook(Book, "Players Guide", null, "Tester"),
            RenderScope.Book,
            [new RenderAct(Act, "Classes and Archetypes", Art, null)],
            [new RenderChapter(Chapter, Act, "Fighter", true, 1, null, null, true)],
            [new RenderScene(Scene, Chapter, Body)],
            new Dictionary<Guid, RenderFigure>(),
            new Dictionary<Guid, RenderImage> { [Art] = new RenderImage(Art, "print", "image/png", 64, 64, key) },
            new Dictionary<Guid, RenderEntry> { [Spell] = new RenderEntry(Spell, "Earthen Armor (Earth)", SpellFields, "A protective layer of stone.\n") },
            new Dictionary<Guid, RenderCitation>());
        var document = ManuscriptAssembler.Assemble(snapshot, template, template.Trims[0], options, RenderTarget.Pdf);
        var pdf = await Host.Services.GetRequiredService<IRenderService>().CompileAsync(document, null, CancellationToken.None);
        return (pdf, await File.ReadAllTextAsync(Path.Combine(pdf.JobDirectory, "main.tex")));
    }

    [OneTimeTearDown]
    public async Task DeleteJob()
    {
        if (_render is not null)
        {
            (await _render).Pdf.Dispose();
        }
    }

    [Test, Order(1)]
    public async Task Compile_WhenTheBookIsShapedLikeThePlayersGuide_ShouldTypesetIt()
    {
        var (pdf, _) = await RenderAsync();

        pdf.Error.ShouldBeNull(pdf.Log);
    }

    // "**Porters' Rebellion:**" stopped an export: pandoc's \textquotesingle is a TS1 glyph, and Bookman's
    // bold TS1 font was not cached. The template curls quotes and dashes as pandoc's +smart did for the original build.
    [Test, Order(2)]
    public async Task Tex_WhenTextHasStraightQuotes_ShouldCurlThemAsTheOriginalBuildDid()
    {
        var (_, tex) = await RenderAsync();

        tex.ShouldNotContain(@"\textquotesingle");
        tex.ShouldNotContain(@"\textquotedbl");
        tex.ShouldContain(@"\textbf{Porters' Rebellion:} the ``free'' folk's war -- it's \emph{``over''}, they say\ldots{}");
        tex.ShouldContain(@"\begin{DndSidebar}{The Porters' Oath}");
        tex.ShouldContain("Say it ``twice''.");
    }

    // The original put the part title above its art on one page; Codex drew the art and the title on two pages.
    [Test, Order(3)]
    public async Task Tex_WhenAPartHasOpenerArt_ShouldSetItsTitleAndArtOnOnePage()
    {
        var (_, tex) = await RenderAsync();

        tex.ShouldMatch(@"\\onecolumn\s*\\begin\{figure\}\[p\]\\centering\s*\\part\{Classes and Archetypes\}[^\n]*\s*\\includegraphics\[[^\]]*\]\{images/[0-9a-f]+\.png\}\s*\\end\{figure\}\s*\\twocolumn");
    }

    // The guide's chapters open with a subheading ("Fighter", then "Overview"); the drop cap went missing.
    [Test, Order(4)]
    public async Task Tex_WhenAChapterOpensWithASubheading_ShouldDropCapItsFirstParagraph()
    {
        var (_, tex) = await RenderAsync();

        tex.ShouldContain(@"\DndDropCapLine{F}{ighters} in Velmara");
    }

    // \DndSpellHeader draws the name and fields; the assembler's fallback heading and field list printed them again.
    [Test, Order(5)]
    public async Task Tex_WhenASpellIsPlaced_ShouldDrawItsHeaderOnce()
    {
        var (_, tex) = await RenderAsync();

        tex.ShouldContain(@"\DndSpellHeader{Earthen Armor (Earth)}{4th-level alteration}{1 round}{Touch}{V, S, M (a small piece of stone)}{1 turn/level}");
        tex.ShouldNotContain(@"\textbf{Earthen Armor (Earth)}");
        tex.ShouldNotContain(@"\begin{description}");
        tex.ShouldContain("A protective layer of stone.");
    }

    // pandoc writes a line break as \\ and a newline, so "*: Dwarfs" on the next line read as \\* and lost its star.
    [Test, Order(6)]
    public async Task Tex_WhenALineBreakIsFollowedByAStar_ShouldKeepTheStar()
    {
        var (_, tex) = await RenderAsync();

        tex.ShouldContain("U: unlimited.\\\\{}\n*: Dwarfs have no limit");
    }
}
