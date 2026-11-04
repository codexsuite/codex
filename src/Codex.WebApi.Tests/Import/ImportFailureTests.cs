using System.Net;
using Codex.Application.Features.Manuscript;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Import;

// A failure after every row is written, just before commit: no book is left, and the run is recorded as failed.
[TestFixture]
public class ImportFailureTests : ApiFixture
{
    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
        services.AddSingleton<IProseStore>(sp => new FailingInTransactionProseStore(ActivatorUtilities.CreateInstance<ProseStore>(sp)));

    [Test]
    public async Task Import_WhenTheWriteFailsBeforeCommit_ShouldLeaveNoBookAndRecordAFailedRun()
    {
        await using var db = await OpenDatabaseAsync();
        var books = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");

        var response = await Client.PostAsync("/api/imports", ImportForms.Form(ImportForms.Folder(ImportForms.SmallFolder)));

        (await response.ReadApiErrorAsync(HttpStatusCode.InternalServerError)).Code.ShouldBe("internal_error");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book")).ShouldBe(books);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book WHERE title = 'The Lantern Road'")).ShouldBe(0);
        (await db.QuerySingleAsync<(string Status, string FileName, string Error, bool HasBook)>(
            "SELECT status, file_name, error, book_id IS NOT NULL FROM book_import ORDER BY created_at DESC LIMIT 1"))
            .ShouldBe(("failed", "markdown-small", "disk full (test double)", false));
    }
}
