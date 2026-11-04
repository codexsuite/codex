using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// Markdown that looks valid but holds what the prose triggers or Postgres reject: it imports, neutralised.
[TestFixture]
public class ImportContentTests : ApiFixture
{
    [Test]
    public async Task Import_WhenCodeHoldsEmbedsAndTextHoldsNulsAndALongEmojiTitle_ShouldImportItNeutralised()
    {
        var title = new string('a', 199) + "😀 tail";
        var prose = "Use `[q](ref:0199c0de-0000-7000-8000-000700000003)` here.\0\n\n```\n::beat{id=\"0199c0de-0000-7000-8000-000700000001\"}\n"
            + "::spell{kb=\"0199c0de-0000-7000-8000-000700000002\"}\n```\n\n- ::beat{id=\"0199c0de-0000-7000-8000-000700000001\"}\n";
        var files = new[]
        {
            ImportForms.Text("b/index.md", $"---\ntitle: {title}\n---\n"),
            ImportForms.Text("b/act-01-a/chapter-01-b/scene-01.md", "---\nid: s1\ntitle: Nul\0 scene\n---\n"),
            ImportForms.Text("b/act-01-a/chapter-01-b/scene-01-prose.md", prose),
        };

        var response = await Client.PostAsync("/api/imports", ImportForms.Form(files));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT title FROM book WHERE id = @BookId", new { summary.BookId })).ShouldBe(new string('a', 199));
        (await db.ExecuteScalarAsync<string>("SELECT title FROM scene WHERE book_id = @BookId", new { summary.BookId })).ShouldBe("Nul scene");
        var body = await db.ExecuteScalarAsync<string>(
            "SELECT p.body FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @BookId", new { summary.BookId });
        body.ShouldBe("Use `[q] (ref:0199c0de-0000-7000-8000-000700000003)` here.\n\n```\n ::beat{id=\"0199c0de-0000-7000-8000-000700000001\"}\n"
            + " ::spell{kb=\"0199c0de-0000-7000-8000-000700000002\"}\n```\n\n- \\::beat{id=\"0199c0de-0000-7000-8000-000700000001\"}\n");
    }
}
