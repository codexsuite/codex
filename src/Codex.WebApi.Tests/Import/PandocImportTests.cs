using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.Contracts.Storage;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Print;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Import;

// One scenario: preview the Players Guide excerpts (Markdown only, images named), import them with the images the
// preview listed, then read the acts' openers, the figures, the spells, the layout and the stored images.
[TestFixture]
public class PandocImportTests : AssetApiFixture
{
    private ImportPreviewDto _preview = null!;
    private ImportSummaryDto _summary = null!;

    private object Ids => new { _summary.BookId, _summary.ImportId };

    [Test, Order(1)]
    public async Task Preview_WhenPlayersGuideExcerpts_ShouldListWhatWillBeCreatedAndTheImagesToSend()
    {
        using var response = await Client.PostAsync("/api/imports/preview", ImportForms.Pandoc());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _preview = (await response.Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        (_preview.Source, _preview.BookTitle).ShouldBe(("pandoc", "Players Guide to The Realm of Velmara"));
        _preview.Creates.Select(c => (c.Item, c.Count, c.Destination)).ShouldBe([
            ("Acts", 3, "Plan · acts"), ("Chapters", 8, "Plan · chapters"), ("Scenes", 8, "Plan · scene cards"),
            ("Scenes with prose", 8, "Write · manuscript"), ("Knowledge base entries", 2, "Knowledge base"),
            ("Figures", 5, "Write · figures"), ("Act openers", 3, "Layout · part opener art"), ("Images", 8, "Assets"),
            ("Layout template", 1, "Layout · fantasy-rpg, drop caps on"),
        ]);
        _preview.Images.ShouldBe(PlayersGuideCampaign.PlacedImages);
        _preview.Skipped.Select(s => s.Path).ShouldBe([
            "players-guide-campaign/Build.ps1", "players-guide-campaign/README.md", PlayersGuideCampaign.UnusedImage, "players-guide-campaign/index.md",
        ]);
    }

    [Test, Order(2)]
    public async Task Import_WhenThePreviewedImagesAreSent_ShouldCreateTheBook()
    {
        using var response = await Client.PostAsync("/api/imports", ImportForms.Pandoc(send: _preview.Images));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        (_summary.Source, _summary.FileName, _summary.Acts, _summary.Chapters, _summary.Scenes, _summary.Entries)
            .ShouldBe(("pandoc", "players-guide-campaign", 3, 8, 8, 2));
        _summary.Skipped.Select(s => s.Path).ShouldBe(_preview.Skipped.Select(s => s.Path));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT status FROM book_import WHERE id = @ImportId", Ids)).ShouldBe("done");
    }

    [Test, Order(3)]
    public async Task Import_WhenPartOpenersHaveArt_ShouldSetEachActsOpener()
    {
        await using var db = await OpenDatabaseAsync();
        var acts = (await db.QueryAsync<(string Title, string? Filename, int? Width)>(
            """
            SELECT a.title, x.filename, x.width_px FROM act a LEFT JOIN asset x ON x.id = a.opener_asset_id
            WHERE a.book_id = @BookId ORDER BY a.position
            """, Ids)).ToList();

        acts.ShouldBe([
            ("Introduction to Velmara", "TOC.png", 160),
            ("Classes and Archetypes", "Chapter 03 Ranger 2.png", 160),
            ("Magic in Velmara", "Chapter 04 Elemental Monks and Their Customs 3.png", 160),
        ]);
    }

    [Test, Order(4)]
    public async Task Import_WhenImagesArePlaced_ShouldWriteFigureRowsTheProseEmbeds()
    {
        await using var db = await OpenDatabaseAsync();
        var figures = (await db.QueryAsync<(string Chapter, string Filename, string Placement, string? Caption, bool InProse)>(
            """
            SELECT c.title, x.filename, f.placement, f.caption,
                   p.body LIKE '%::figure{id="' || f.id || '"}%' AS InProse
            FROM figure f JOIN scene s ON s.id = f.scene_id JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id
            JOIN asset x ON x.id = f.asset_id JOIN prose p ON p.scene_id = s.id
            WHERE s.book_id = @BookId AND f.archived_at IS NULL ORDER BY a.position, c.position, position(f.id::text IN p.body)
            """, Ids)).ToList();

        figures.Select(f => (f.Chapter, f.Filename, f.Placement)).ShouldBe([
            ("The History of Velmara", "Background_The Age_of_Harvest.png", "column"),
            ("The History of Velmara", "BackGround The Age of the Guild.png", "float-top"),
            ("The History of Velmara", "Background The Age of Elemental Renewal 1.png", "column"),
            ("The Themes of Velmara", "Chapter 01 - end.png", "float-top"),
            ("Fighter", "Chapter 03 FIghter 3.png", "column"),
        ]);
        figures[1].Caption.ShouldBe("The drifting Tower of the Cartographer's Guild");
        figures.ShouldAllBe(f => f.InProse);
    }

    [Test, Order(5)]
    public async Task Import_WhenImagesAreStored_ShouldKeepTheOriginalsAndMakeRenditions()
    {
        await using var db = await OpenDatabaseAsync();
        var assets = (await db.QueryAsync<(Guid Id, string StorageKey, string Source)>(
            "SELECT id, storage_key, meta->>'source' FROM asset WHERE book_id = @BookId ORDER BY filename", Ids)).ToList();

        assets.Count.ShouldBe(8);
        assets.Select(a => a.Source).Order(StringComparer.Ordinal).ShouldBe(PlayersGuideCampaign.PlacedImages.Order(StringComparer.Ordinal));
        var blobs = Host.Services.GetRequiredService<IBlobStore>();
        foreach (var asset in assets)
        {
            (await blobs.ExistsAsync(asset.StorageKey, CancellationToken.None)).ShouldBeTrue();
        }
        (await SettledAsync(assets[0].Id)).Status.ShouldBe("ready");
    }

    [Test, Order(6)]
    public async Task Import_WhenSpellHeadersAreMapped_ShouldMakeSpellsEntriesTheProseNames()
    {
        await using var db = await OpenDatabaseAsync();
        var spells = (await db.QueryAsync<(Guid Id, string Name, string Range, string FieldDefs, string Body)>(
            """
            SELECT e.id, e.name, e.fields->>'range', k.field_defs::text, e.body FROM kb_entry e JOIN kb_category k ON k.id = e.category_id
            WHERE e.book_id = @BookId AND k.name = 'Spells' ORDER BY e.position
            """, Ids)).ToList();

        spells.Select(s => (s.Name, s.Range)).ShouldBe([("Earthen Armor (Earth)", "Touch"), ("Mineral Infusion (Earth)", "Touch")]);
        spells[0].FieldDefs.ShouldContain("\"key\": \"level-school\"");
        spells[0].Body.ShouldStartWith("This spell wraps the caster in stone");
        var prose = await db.ExecuteScalarAsync<string>(
            "SELECT p.body FROM prose p JOIN scene s ON s.id = p.scene_id JOIN chapter c ON c.id = s.chapter_id WHERE s.book_id = @BookId AND c.title = 'Introduction'", Ids);
        prose!.ShouldEndWith($"::spell{{kb=\"{spells[0].Id}\"}}\n\n::spell{{kb=\"{spells[1].Id}\"}}\n");
    }

    [Test, Order(7)]
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

    [Test, Order(8)]
    public async Task Undo_WhenNothingWasWrittenSince_ShouldBeOfferedAndMarkTheBookDeleted()
    {
        var banner = (await Client.GetFromJsonAsync<BookImportDto>($"/api/books/{_summary.BookId}/import"))!;
        banner.CanUndo.ShouldBeTrue();

        using var response = await Client.PostAsync($"/api/imports/{_summary.ImportId}/undo", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = await OpenDatabaseAsync();
        // Soft delete (#85): the book is marked deleted and hidden, and its images stay with it.
        (await db.ExecuteScalarAsync<bool>("SELECT deleted_at IS NOT NULL FROM book WHERE id = @BookId", Ids)).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM asset WHERE book_id = @BookId", Ids)).ShouldBeGreaterThan(0);
        (await Client.GetAsync($"/api/books/{_summary.BookId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
