using System.Net;
using Codex.Application.Features.Print;
using Codex.Contracts;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Print;

// The job queue's rules, driven through the public ProcessNextAsync with the job row set up directly:
// a fresh running job is left alone, a stale one is taken over, and a job that keeps stalling is given up on.
// (The hosted worker may take a job first, since its poll is independent; the tests assert end states, not who ran it.)
[TestFixture]
public class AssetWorkerTests : AssetApiFixture
{
    private Guid _assetId;

    private AssetRenditionWorker Worker => Host.Services.GetRequiredService<AssetRenditionWorker>();

    private async Task SetJobAsync(string status, int startedMinutesAgo, int attempts)
    {
        await using var connection = await OpenDatabaseAsync();
        await connection.ExecuteAsync(
            "UPDATE asset_rendition_job SET status = @status, started_at = now() - make_interval(mins => @ago), attempts = @attempts, error = NULL WHERE asset_id = @id",
            new { status, ago = startedMinutesAgo, attempts, id = _assetId });
    }

    private async Task<(string Status, int Attempts, string? Error)> JobAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        return await connection.QuerySingleAsync<(string, int, string?)>(
            "SELECT status, attempts, error FROM asset_rendition_job WHERE asset_id = @id", new { id = _assetId });
    }

    [Test, Order(1)]
    public async Task Upload_ShouldBeRenderedOnTheFirstAttempt()
    {
        _assetId = (await UploadOkAsync(SeedIds.Books.PlayersGuide, "small.png", TestImages.Photo(120, 80), "image/png")).Id;

        (await SettledAsync(_assetId)).Status.ShouldBe("ready");
        (await JobAsync()).ShouldBe(("succeeded", 1, null));
    }

    [Test, Order(2)]
    public async Task FreshRunningJob_ShouldNotBeTakenOver()
    {
        await SetJobAsync("running", 0, 1);

        var worked = await Worker.ProcessNextAsync(CancellationToken.None);

        worked.ShouldBeFalse();
        (await JobAsync()).ShouldBe(("running", 1, null));
    }

    [Test, Order(3)]
    public async Task StaleRunningJob_ShouldBeTakenOverAndFinished()
    {
        await SetJobAsync("running", 10, 1);

        await Worker.ProcessNextAsync(CancellationToken.None);

        (await SettledAsync(_assetId)).Status.ShouldBe("ready");
        (await JobAsync()).ShouldBe(("succeeded", 2, null));
    }

    [Test, Order(4)]
    public async Task StaleJobThatHadItsAttempts_ShouldBeGivenUpOn()
    {
        await SetJobAsync("running", 10, AssetRenditionWorker.MaxAttempts);

        var worked = await Worker.ProcessNextAsync(CancellationToken.None);

        var job = await JobAsync();
        worked.ShouldBeFalse();
        job.Status.ShouldBe("failed");
        job.Error.ShouldStartWith("Gave up after 3 attempts");
        job.Attempts.ShouldBe(3);
        (await SettledAsync(_assetId)).Status.ShouldBe("failed");
    }

    [Test, Order(5)]
    public async Task Requeue_AfterGivingUp_ShouldStartAgain()
    {
        using var response = await Client.PostAsync($"/api/assets/{_assetId}/renditions", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await SettledAsync(_assetId)).Status.ShouldBe("ready");
        (await JobAsync()).ShouldBe(("succeeded", 1, null));
    }
}
