using System.Net;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

// J17: a live print export blocks Archive ("An export is running for this book"); a finished one does not.
[TestFixture]
public class BookArchiveBlockedTests : ApiFixture
{
    private async Task AddPrintJobAsync(Guid book, string status)
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync(
            """
            INSERT INTO export_job (book_id, kind, template_version_id, trim_size, status, output_asset_id, created_by)
            SELECT @book, 'print', v.id, '6x9', @status,
                   CASE WHEN @status = 'succeeded' THEN (SELECT id FROM asset LIMIT 1) END, @owner
            FROM layout_template_version v LIMIT 1
            """, new { book, status, owner = SeedIds.Users.Jd });
    }

    [TestCase("running")]
    [TestCase("queued")]
    public async Task Archive_WhenPrintExportIsLive_ShouldAnswerBlockedAndKeepTheBookShelved(string status)
    {
        var book = status == "running" ? SeedIds.Books.Undertow : SeedIds.Books.ProductEngineeringV1;
        await AddPrintJobAsync(book, status);

        var response = await Client.PostAsync($"/api/books/{book}/archive", null);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.UnprocessableEntity);
        error.Code.ShouldBe(ApiErrorCodes.Blocked);
        error.Error.ShouldContain("An export is running");
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<DateTimeOffset?>("SELECT archived_at FROM book WHERE id = @book", new { book })).ShouldBeNull();
    }

    [Test]
    public async Task Archive_WhenExportFailed_ShouldArchive()
    {
        await AddPrintJobAsync(SeedIds.Books.SaltwaterLedger, "failed");

        var response = await Client.PostAsync($"/api/books/{SeedIds.Books.SaltwaterLedger}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
