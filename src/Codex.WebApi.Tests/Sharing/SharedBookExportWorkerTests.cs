using Codex.Application.Features.Print.Export;
using Codex.Contracts;
using Codex.Contracts.Events;
using Codex.TestSupport.Fixtures;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Sharing;

// Background work re-checks the role when it runs (#80): an export queued by someone who has since lost write access
// to the book fails cleanly when the worker reaches it, instead of building a copy of the manuscript for them.
// (The test host's own worker may claim the job first, so the test waits for the job's failed event, whoever ran it.)
[TestFixture]
public class SharedBookExportWorkerTests : ApiFixture
{
    [Test]
    public async Task ProcessNext_WhenTheRequesterLostWriteAccess_ShouldFailTheExport()
    {
        await using var db = await OpenDatabaseAsync();
        var job = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO export_job (book_id, kind, created_by) VALUES (@book, 'source', @ana) RETURNING id",
            new { book = SeedIds.Books.PlayersGuide, ana = SeedIds.Users.Ana });
        await db.ExecuteAsync(
            "DELETE FROM book_grant WHERE book_id = @series AND user_id = @ana", new { series = SeedIds.Series.ForsakenGods, ana = SeedIds.Users.Ana });

        var failed = new TaskCompletionSource<JobProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = Host.Services.GetRequiredService<IBookEvents>().Subscribe(SeedIds.Books.PlayersGuide, e =>
        {
            if (e is JobProgress progress && progress.JobId == job && progress.State == "failed")
            {
                failed.TrySetResult(progress);
            }
            return Task.CompletedTask;
        });

        await Host.Services.GetRequiredService<ExportWorker>().ProcessNextAsync(CancellationToken.None);

        await Ai.AnthropicDraftRows.WithinPatienceAsync(failed.Task);
        (await failed.Task).Message.ShouldBe("The person who asked for this export can no longer edit the book.");
        var row = await db.QuerySingleAsync<(string Status, string? Error, Guid? Output)>(
            "SELECT status, error, output_asset_id FROM export_job WHERE id = @job", new { job });
        row.ShouldBe(("failed", "The person who asked for this export can no longer edit the book.", (Guid?)null));
    }
}
