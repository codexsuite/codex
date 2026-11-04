using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Import;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// Undo import is offered until the book is written in; after a prose save it is hidden and refused as blocked.
[TestFixture]
public class ImportUndoBlockedTests : ApiFixture
{
    private ImportSummaryDto _summary = null!;

    [Test, Order(1)]
    public async Task Banner_WhenJustImported_ShouldOfferUndo()
    {
        var response = await Client.PostAsync("/api/imports", ImportForms.Form(ImportForms.Folder(ImportForms.SmallFolder)));
        _summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;

        (await Client.GetFromJsonAsync<BookImportDto>($"/api/books/{_summary.BookId}/import"))!.CanUndo.ShouldBeTrue();
    }

    [Test, Order(2)]
    public async Task Undo_WhenProseWasSavedSinceTheImport_ShouldBeHiddenAndRefusedAsBlocked()
    {
        await using var db = await OpenDatabaseAsync();
        var scene = await db.QuerySingleAsync<(Guid Id, string Body, int Version)>(
            "SELECT p.scene_id, p.body, p.version FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @BookId AND s.import_ref = 'ch03-s01'",
            new { _summary.BookId });
        var saved = await Client.PutAsJsonAsync($"/api/scenes/{scene.Id}/prose", new SaveProseRequest(scene.Body + "\nA new line.\n", scene.Version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Client.GetFromJsonAsync<BookImportDto>($"/api/books/{_summary.BookId}/import"))!.CanUndo.ShouldBeFalse();
        var response = await Client.PostAsync($"/api/imports/{_summary.ImportId}/undo", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.UnprocessableEntity)).Code.ShouldBe("blocked");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book WHERE id = @BookId", new { _summary.BookId })).ShouldBe(1);
    }
}
