using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Import;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// A codex[] list far past the entry cap: the first MaxEntries names become entries, the rest are reported as skipped.
[TestFixture]
public class ImportEntryCapTests : ApiFixture
{
    [Test]
    public async Task Import_WhenMoreNamesThanTheEntryCap_ShouldKeepTheFirstAndReportTheRest()
    {
        var names = string.Join(", ", Enumerable.Range(0, ImportLimits.MaxEntries + 500).Select(i => $"Name {i}"));
        var files = new[]
        {
            ImportForms.Text("b/index.md", "---\ntitle: Many names\n---\n"),
            ImportForms.Text("b/act-01-a/chapter-01-b/scene-01.md", $"---\nid: s1\ncodex: [{names}]\n---\n"),
            ImportForms.Text("b/act-01-a/chapter-01-b/scene-02.md", "---\nid: s2\ncodex: [Name 1, Name 2400]\n---\n"),
        };

        var response = await Client.PostAsync("/api/imports", ImportForms.Form(files));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        summary.Entries.ShouldBe(ImportLimits.MaxEntries);
        summary.Skipped.Single().Reason.ShouldBe("Only the first 2000 knowledge base entries are imported; 500 more names were left out");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM scene_kb k JOIN scene s ON s.id = k.scene_id WHERE s.book_id = @BookId", new { summary.BookId }))
            .ShouldBe(ImportLimits.MaxEntries + 1);
    }
}
