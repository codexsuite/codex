using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// One scenario: preview the Product Engineering (v1) Word document, import it, and read what landed.
[TestFixture]
public class DocxImportTests : ApiFixture
{
    private ImportSummaryDto _summary = null!;

    private object Ids => new { _summary.BookId, _summary.ImportId };

    private static MultipartFormDataContent Form() =>
        ImportForms.Form([("Product Engineering.docx", ProductEngineeringV1.Docx())], source: "docx");

    [Test, Order(1)]
    public async Task Preview_WhenDocx_ShouldCountChaptersAndScenesAndListWhatIsLeftOut()
    {
        var response = await Client.PostAsync("/api/imports/preview", Form());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        preview.BookTitle.ShouldBe("Product Engineering");
        preview.Creates.Select(c => (c.Item, c.Count)).ShouldBe([("Acts", 1), ("Chapters", 4), ("Scenes", 6), ("Scenes with prose", 6)]);
        preview.Skipped.Select(s => s.Path).ShouldBe(["Images", "Comments"]);
    }

    [Test, Order(2)]
    public async Task Import_WhenDocx_ShouldMapHeadingsToChaptersAndSectionBreaksToScenes()
    {
        var response = await Client.PostAsync("/api/imports", Form());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        (_summary.Source, _summary.FileName, _summary.Acts, _summary.Chapters, _summary.Scenes).ShouldBe(("docx", "Product Engineering.docx", 1, 4, 6));
        await using var db = await OpenDatabaseAsync();
        (await db.QueryAsync<(string Act, string Chapter, bool Numbered, string? Scene)>(
            """
            SELECT a.title, c.title, c.is_numbered, s.title
            FROM scene s JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id
            WHERE s.book_id = @BookId ORDER BY a.position, c.position, s.position
            """, Ids)).ShouldBe([
            ("Act 1", "Front matter", false, null),
            ("Act 1", "Preface", true, null),
            ("Act 1", "A Brief History of Software Practice", true, "The beginnings in Agile"),
            ("Act 1", "A Brief History of Software Practice", true, "Feature factories"),
            ("Act 1", "Owning Outcomes", true, null),
            ("Act 1", "Owning Outcomes", true, null),
        ]);
    }

    [Test, Order(3)]
    public async Task Import_WhenDocx_ShouldStoreProseWithEmphasisAndOneImportRevision()
    {
        await using var db = await OpenDatabaseAsync();

        var prose = await db.QuerySingleAsync<(string Body, string Source)>(
            """
            SELECT p.body, r.source FROM prose p JOIN scene s ON s.id = p.scene_id JOIN chapter c ON c.id = s.chapter_id
            JOIN prose_revision r ON r.scene_id = p.scene_id
            WHERE s.book_id = @BookId AND c.title = 'Preface'
            """, Ids);

        prose.Body.ShouldBe("For ten years I watched good teams ship the wrong thing, *on time*.\n\nThis book is about the **other** way to work.\n");
        prose.Source.ShouldBe("import");
    }
}
