using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Application.Features.Print.Render;
using Codex.Application.Prose;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Import;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// #13 phase 2 acceptance: import the made-up campaign folder in Fixtures/import/players-guide-campaign (the pandoc
// source, through POST /api/imports/preview and POST /api/imports, with the images the preview lists), check #13's
// import mapping, and export the whole book with the Fantasy RPG template.
[TestFixture]
public partial class PlayersGuideRoundTripTests : ApiFixture
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(30);

    private static string Source => PlayersGuideCampaign.Folder;

    private Guid _bookId;

    private object Ids => new { BookId = _bookId };

    [Test, Order(1)]
    public async Task Import_WhenThePlayersGuideFolderIsUploaded_ShouldCreateTheBook()
    {
        using var client = Host.CreateClient();
        client.Timeout = Patience;
        using var preview = await client.PostAsync("/api/imports/preview", ImportForms.Pandoc(Source));
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var images = (await preview.Content.ReadFromJsonAsync<ImportPreviewDto>())!.Images!;
        images.ShouldNotBeEmpty();

        using var response = await client.PostAsync("/api/imports", ImportForms.Pandoc(Source, images));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        _bookId = summary.BookId;
        summary.Skipped.ShouldNotContain(s => s.Reason.Contains("The image was left out", StringComparison.Ordinal));
    }

    [Test, Order(2)]
    public async Task Import_WhenPartsHaveOpeners_ShouldMakeActsWithTheirArtAndUnnumberedChapters()
    {
        await using var db = await OpenDatabaseAsync();
        var acts = (await db.QueryAsync<(string Title, Guid? Opener)>(
            "SELECT title, opener_asset_id FROM act WHERE book_id = @BookId ORDER BY position", Ids)).ToList();
        var chapters = (await db.QueryAsync<string>("SELECT title FROM chapter WHERE book_id = @BookId", Ids)).ToList();

        acts.Count.ShouldBe(3);
        acts.ShouldAllBe(a => a.Opener != null);
        acts[0].Title.ShouldBe("Introduction to Velmara");
        chapters.ShouldContain("Fighter");
        chapters.ShouldNotContain(c => HandNumber().IsMatch(c));
    }

    [Test, Order(3)]
    public async Task Import_WhenTheChaptersPlaceImagesAndSpells_ShouldMakeFiguresAndSpellsEntries()
    {
        await using var db = await OpenDatabaseAsync();
        var figures = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM figure f JOIN scene s ON s.id = f.scene_id WHERE s.book_id = @BookId AND f.archived_at IS NULL", Ids);
        var spells = (await db.QueryAsync<(string Name, string? Range)>(
            """
            SELECT e.name, e.fields->>'range' FROM kb_entry e JOIN kb_category k ON k.id = e.category_id
            WHERE e.book_id = @BookId AND k.name = 'Spells'
            """, Ids)).ToList();

        figures.ShouldBeGreaterThanOrEqualTo(5);
        spells.Count.ShouldBe(2);
        spells.ShouldContain(("Earthen Armor (Earth)", "Touch"));
    }

    [Test, Order(4)]
    public async Task Import_WhenTheProseIsStored_ShouldKeepTablesAndLeaveNoLatexOutsideAFence()
    {
        await using var db = await OpenDatabaseAsync();
        var bodies = (await db.QueryAsync<string>(
            "SELECT p.body FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @BookId", Ids)).ToList();

        bodies.ShouldContain(b => b.Contains("\n| Kit | Recommendations for Velmara |\n| --- | --- |\n", StringComparison.Ordinal));
        foreach (var body in bodies)
        {
            var document = ProseDocument.Parse(body);
            document.Blocks
                .Where(b => b.Kind is ProseBlockKind.Paragraph or ProseBlockKind.Heading or ProseBlockKind.List or ProseBlockKind.Quote)
                .Select(b => body[b.Start..b.End])
                .Where(text => LatexCommand().IsMatch(text) && !text.StartsWith('|'))
                .ShouldBeEmpty();
        }
    }

    [Test, Order(5)]
    public async Task Import_WhenTheSourceUsedDropCaps_ShouldChooseFantasyRpgWithDropCapsOn()
    {
        await using var db = await OpenDatabaseAsync();
        var layout = await db.QuerySingleAsync<(string Slug, string? DropCaps)>(
            """
            SELECT t.slug, o.options->>'drop-caps' FROM book_layout l JOIN layout_template t ON t.id = l.template_id
            JOIN book_layout_template o ON o.book_id = l.book_id AND o.template_id = l.template_id WHERE l.book_id = @BookId
            """, Ids);

        layout.ShouldBe(("fantasy-rpg", "true"));
    }

    [Test, Order(6)]
    public async Task Export_WhenTheImportedBookRendersWithTheFantasyTemplate_ShouldWriteAPdfOfEveryChapter()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            // The export places renditions: the worker makes them for every image the import stored.
            await ImportRenditions.SettledAsync(db, _bookId, Patience);
        }

        var renderer = Host.Services.GetRequiredService<IRenderService>();

        using var pdf = await renderer.RenderPdfAsync(new RenderRequest(_bookId, RenderScope.Book), null, CancellationToken.None);

        pdf.Error.ShouldBeNull(pdf.Log);

        pdf.Document.TemplateSlug.ShouldBe("fantasy-rpg");
        (await File.ReadAllBytesAsync(pdf.PdfPath!)).Take(5).ShouldBe("%PDF-"u8.ToArray());
        var blocks = pdf.Document.Ast["blocks"]!.AsArray();
        var chapters = blocks.Count(b => b!["t"]!.GetValue<string>() == "Header" && b["c"]![0]!.GetValue<int>() == 2);
        var parts = blocks.Where(b => b!["t"]!.GetValue<string>() == "Header" && b["c"]![0]!.GetValue<int>() == 1).ToList();
        parts.ShouldAllBe(p => p!["c"]![1]![2]!.AsArray().Any(kv => kv![0]!.GetValue<string>() == "opener"));
        blocks.Count(b => b!["t"]!.GetValue<string>() == "Figure").ShouldBeGreaterThan(0);
        pdf.Document.Flags.Where(f => f.Kind == RenderFlag.MissingAsset).ShouldBeEmpty();
        // HTML comments are kept as :::comment blocks, which print leaves out (and flags); nothing else is unknown.
        pdf.Document.Flags.Where(f => f.Kind == RenderFlag.UnknownDirective).ShouldAllBe(f => f.Detail == ":::comment");
        (chapters, parts.Count).ShouldBe((8, 3));
        pdf.Document.RawLatex.ShouldBeEmpty();
    }

    [GeneratedRegex(@"^\d+(\.\d+)+\s")]
    private static partial Regex HandNumber();

    [GeneratedRegex(@"\\[A-Za-z]+")]
    private static partial Regex LatexCommand();
}
