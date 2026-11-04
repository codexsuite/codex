using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// One scenario: preview the Product Engineering (v1) Novelcrafter export, import it, read what landed, then Undo import.
[TestFixture]
public class NovelcrafterImportTests : ApiFixture
{
    private ImportSummaryDto _summary = null!;

    private object Ids => new { _summary.BookId, _summary.ImportId };

    private static MultipartFormDataContent Form() =>
        ImportForms.Form([("product-engineering-v1.zip", ProductEngineeringV1.NovelcrafterZip())], source: "novelcrafter");

    [Test, Order(1)]
    public async Task Preview_WhenNovelcrafterExport_ShouldListWhatWillBeCreatedAndWriteNothing()
    {
        await using var before = await OpenDatabaseAsync();
        var books = await before.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");

        var response = await Client.PostAsync("/api/imports/preview", Form());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        preview.Source.ShouldBe("novelcrafter");
        preview.BookTitle.ShouldBe("Product Engineering");
        preview.Creates.Select(c => (c.Item, c.Count)).ShouldBe([
            ("Acts", 2), ("Chapters", 3), ("Scenes", 4), ("Scene beats", 4), ("Scenes with prose", 3), ("Knowledge base entries", 4), ("Style guide", 1),
        ]);
        preview.Skipped.Select(s => s.Path).ShouldBe(["product-engineering-v1/research/sources.txt"]);
        (await before.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book")).ShouldBe(books);
    }

    [Test, Order(2)]
    public async Task Import_WhenNovelcrafterExport_ShouldAnswer201WithTheSummary()
    {
        var response = await Client.PostAsync("/api/imports", Form());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        (_summary.Source, _summary.FileName, _summary.Acts, _summary.Chapters, _summary.Scenes, _summary.Entries)
            .ShouldBe(("novelcrafter", "product-engineering-v1.zip", 2, 3, 4, 4));
        await using var db = await OpenDatabaseAsync();
        (await db.QuerySingleAsync<(string Status, string Source)>(
            "SELECT status, source FROM book_import WHERE id = @ImportId", Ids)).ShouldBe(("done", "novelcrafter"));
    }

    [Test, Order(3)]
    public async Task Import_WhenNovelcrafterExport_ShouldKeepKsuidsInExternalId()
    {
        await using var db = await OpenDatabaseAsync();
        var entries = (await db.QueryAsync<(string Name, string? ExternalId, string Category)>(
            """
            SELECT e.name, e.external_id, c.name FROM kb_entry e JOIN kb_category c ON c.id = e.category_id
            WHERE e.book_id = @BookId ORDER BY c.position, e.position
            """, Ids)).ToList();

        entries.ShouldBe([
            ("Kent Beck", ProductEngineeringV1.BeckKsuid, "Characters"),
            ("Feature Factory", ProductEngineeringV1.FeatureFactoryKsuid, "Other"),
            ("Goodhart's Law", ProductEngineeringV1.GoodhartKsuid, "Other"),
            ("Agile Manifesto", null, "Imported"),
        ]);
    }

    [Test, Order(4)]
    public async Task Import_WhenNovelcrafterExport_ShouldKeepAliasesSummaryAndBody()
    {
        await using var db = await OpenDatabaseAsync();

        var beck = await db.QuerySingleAsync<(string[] Aliases, string? Summary, string Body)>(
            "SELECT aliases, summary, body FROM kb_entry WHERE book_id = @BookId AND external_id = @id",
            new { _summary.BookId, id = ProductEngineeringV1.BeckKsuid });

        beck.Aliases.ShouldBe(["Beck", "KB"]);
        beck.Summary.ShouldBe("Creator of Extreme Programming.");
        beck.Body.ShouldBe("Wrote *Extreme Programming Explained* and co-signed the Agile Manifesto.\n");
    }

    [Test, Order(5)]
    public async Task Import_WhenSceneNamesEntriesByKsuidOrName_ShouldLinkThem()
    {
        await using var db = await OpenDatabaseAsync();

        var links = (await db.QueryAsync<(string ImportRef, string Entry)>(
            """
            SELECT s.import_ref, e.name FROM scene_kb sk
            JOIN scene s ON s.id = sk.scene_id JOIN kb_entry e ON e.id = sk.kb_entry_id
            WHERE s.book_id = @BookId ORDER BY s.import_ref, e.name
            """, Ids)).ToList();

        links.ShouldBe([
            ("ch02-s01", "Agile Manifesto"), ("ch02-s01", "Kent Beck"),
            ("ch02-s02", "Feature Factory"), ("ch02-s02", "Goodhart's Law"),
            ("ch03-s01", "Goodhart's Law"),
        ]);
    }

    [Test, Order(6)]
    public async Task Import_WhenNovelcrafterExport_ShouldMapStructureStyleGuideAndStoredProse()
    {
        await using var db = await OpenDatabaseAsync();

        var book = await db.QuerySingleAsync<(string Title, string? Subtitle, string? Author, string? StyleGuide)>(
            "SELECT title, subtitle, author_name, style_guide FROM book WHERE id = @BookId", Ids);
        book.ShouldBe(("Product Engineering", "Shipping the right thing", "Joel Dickson", "# Writing rules\n\nPlain words. Evidence before anecdote. Name the team, never the person."));
        (await db.QueryAsync<string>("SELECT title FROM act WHERE book_id = @BookId ORDER BY position", Ids)).ShouldBe(["Introduction", "The Product Engineer"]);
        var prose = await db.QuerySingleAsync<(string Body, int Version)>(
            "SELECT p.body, p.version FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @BookId AND s.import_ref = 'ch01-s01'", Ids);
        prose.Body.ShouldEndWith("For ten years I watched good teams ship the wrong thing, *on time*.\n\nThis book is about the **other** way to work.\n");
        prose.Version.ShouldBe(1);
    }

    [Test, Order(7)]
    public async Task Undo_WhenNovelcrafterImport_ShouldMarkTheBookDeleted()
    {
        var response = await Client.PostAsync($"/api/imports/{_summary.ImportId}/undo", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM book WHERE id = @BookId AND deleted_at IS NOT NULL", Ids)).ShouldBe(1);
        (await Client.GetAsync($"/api/books/{_summary.BookId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
