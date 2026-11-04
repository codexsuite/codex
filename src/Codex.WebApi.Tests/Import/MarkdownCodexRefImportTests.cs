using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// A manuscript whose codex lists name their entries by path: the entries arrive with their text, category, aliases,
// always-in-context flag and url, and same-named files stay two entries (issue #79).
[TestFixture]
public class MarkdownCodexRefImportTests : ApiFixture
{
    [Test]
    public async Task Import_WhenCodexItemsNamePaths_ShouldCreateEntriesWithTheirTextCategoriesAndSources()
    {
        var notes = "---\nid: {0}\ncodex:\n  - name: Scene 1 voice notes\n    path: scene-01-notes.md\n---\n";
        var files = new[]
        {
            ImportForms.Text("pe/manuscript/index.md", "---\ntitle: Product Engineering\n---\n"),
            ImportForms.Text("pe/manuscript/act-01-a/chapter-14-x/index.md", "---\ntitle: Fourteen\ncodex:\n  - name: Goodhart's Law\n    path: ../../../other/goodharts-law/entry.md\n---\n"),
            ImportForms.Text("pe/manuscript/act-01-a/chapter-14-x/scene-01.md", string.Format(notes, "a")),
            ImportForms.Text("pe/manuscript/act-01-a/chapter-14-x/scene-01-notes.md", "Fourteen notes.\n"),
            ImportForms.Text("pe/manuscript/act-01-a/chapter-15-y/index.md", "---\ntitle: Fifteen\n---\n"),
            ImportForms.Text("pe/manuscript/act-01-a/chapter-15-y/scene-01.md", string.Format(notes, "b")),
            ImportForms.Text("pe/manuscript/act-01-a/chapter-15-y/scene-01-notes.md", "Fifteen notes.\n"),
            ImportForms.Text("pe/other/goodharts-law/entry.md",
                "---\nid: 0ujsswThIGTUYm2K8FjOOfXtY1K\nname: Goodhart's Law\ntags: [Laws]\naliases: [story.Goodhart]\nalwaysIncludeInContext: true\n---\n\nA measure that is a target.\n"),
            ImportForms.Text("pe/blog/post/post.md", "# A post\n\nBlog text.\n"),
        };
        var withUrl = files.Append(ImportForms.Text("pe/manuscript/act-01-a/chapter-15-y/scene-02.md",
            "---\nid: c\ncodex:\n  - name: A post\n    path: ../../../blog/post/post.md\n    url: https://example.com/post\n---\n")).ToList();

        var response = await Client.PostAsync("/api/imports", ImportForms.Form(withUrl));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        summary.Entries.ShouldBe(4);
        await using var db = await OpenDatabaseAsync();
        var entries = (await db.QueryAsync<(string Name, string Category, string Body, string? Url, bool Always, string[] Aliases)>(
            """
            SELECT e.name, c.name, e.body, e.import_url, e.always_in_context, e.aliases
            FROM kb_entry e JOIN kb_category c ON c.id = e.category_id WHERE e.book_id = @BookId ORDER BY e.name
            """, new { summary.BookId })).ToList();
        entries.Select(e => (e.Name, e.Category)).ShouldBe([
            ("A post", "Imported"), ("Goodhart's Law", "Laws"), ("Scene 1 voice notes (Fifteen)", "Imported"), ("Scene 1 voice notes (Fourteen)", "Imported")]);
        entries[0].Url.ShouldBe("https://example.com/post");
        entries[0].Body.ShouldBe("Blog text.\n");
        entries[1].Always.ShouldBeTrue();
        entries[1].Aliases.ShouldBe(["story.Goodhart"]);
        entries[1].Body.ShouldBe("A measure that is a target.\n");
        entries[2].Body.ShouldBe("Fifteen notes.\n");
        entries[3].Body.ShouldBe("Fourteen notes.\n");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene_kb k JOIN scene s ON s.id = k.scene_id WHERE s.book_id = @BookId", new { summary.BookId }))
            .ShouldBe(3);
    }
}
