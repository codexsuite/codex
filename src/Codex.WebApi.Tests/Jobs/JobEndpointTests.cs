using System.Net.Http.Json;
using Codex.Contracts.Jobs;
using Codex.WebApi.Tests.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.Jobs;

[TestFixture]
public class JobEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _running;
    private Guid _done;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        await using var connection = await OpenDatabaseAsync();
        _running = await connection.QuerySingleAsync<Guid>(
            "INSERT INTO export_job (book_id, kind, status, step, progress, started_at) VALUES (@book, 'source', 'running', 'Assembling', 0.4, now()) RETURNING id",
            new { book = _book });
        var asset = await connection.QuerySingleAsync<Guid>(
            "INSERT INTO asset (owner_id, book_id, kind, filename, mime, bytes, storage_key) VALUES (@owner, @book, 'archive', 'out.zip', 'application/zip', 1, 'k') RETURNING id",
            new { owner = Codex.Contracts.SeedIds.Users.Jd, book = _book });
        _done = await connection.QuerySingleAsync<Guid>(
            "INSERT INTO export_job (book_id, kind, status, progress, output_asset_id, started_at, finished_at) VALUES (@book, 'source', 'succeeded', 1, @asset, now(), now()) RETURNING id",
            new { book = _book, asset });
    }

    [Test]
    public async Task Get_ShouldReturnState()
    {
        var running = (await Client.GetFromJsonAsync<JobDto>($"/api/jobs/{_running}"))!;
        running.Id.ShouldBe(_running);
        running.BookId.ShouldBe(_book);
        running.Kind.ShouldBe("source");
        running.State.ShouldBe("running");
        running.Percent.ShouldBe(40);
        running.Message.ShouldBe("Assembling");
        running.ResultUrl.ShouldBeNull();

        var done = (await Client.GetFromJsonAsync<JobDto>($"/api/jobs/{_done}"))!;
        done.State.ShouldBe("done");
        done.Percent.ShouldBe(100);
        done.ResultUrl.ShouldNotBeNull();
        done.UpdatedAt.ShouldBeGreaterThanOrEqualTo(done.CreatedAt);
    }

    [Test]
    public async Task Get_WhenUnknown_404()
    {
        var error = await ApiErrors.ReadApiErrorAsync(await Client.GetAsync($"/api/jobs/{Guid.NewGuid()}"), System.Net.HttpStatusCode.NotFound);

        error.Code.ShouldBe("not_found");
    }
}
