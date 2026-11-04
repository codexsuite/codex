using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// get_job for an export job: the export worker is not built yet (L7.5), so the rows are inserted here. The tool must say
// what GET /api/jobs/{id} says. One scenario on its own clone.
[TestFixture]
[CancelAfter(120000)]
public class McpExportJobTests
{
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [TestCase("running", 0.4, "Assembling", null, "running")]
    [TestCase("queued", null, null, null, "queued")]
    [TestCase("failed", 0.9, "Typesetting", "Tectonic stopped.", "failed")]
    [TestCase("cancelled", null, null, null, "failed")]
    public async Task GetJob_WhenTheJobIsAnExportJob_ShouldAnswerAsTheHttpApiDoes(
        string status, double? progress, string? step, string? error, string state)
    {
        var id = await InsertSourceJobAsync(status, progress, step, error);

        var job = await Client.OkAsync("get_job", new { job_id = id });

        job.GetProperty("job_id").GetGuid().ShouldBe(id);
        job.GetProperty("kind").GetString().ShouldBe("source");
        job.GetProperty("state").GetString().ShouldBe(state);
        var api = await _server.ApiAsync($"/api/jobs/{id}");
        api.GetProperty("state").GetString().ShouldBe(state);
        api.GetProperty("kind").GetString().ShouldBe("source");
        job.GetProperty("percent").GetInt32().ShouldBe(api.GetProperty("percent").GetInt32());
        Text(job, "message").ShouldBe(Text(api, "message"));
    }

    [Test]
    public async Task GetJob_WhenNoJobHasTheId_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("get_job", new { job_id = Guid.CreateVersion7() });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    private static string? Text(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : null;

    private async Task<Guid> InsertSourceJobAsync(string status, double? progress, string? step, string? error)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO export_job (book_id, kind, status, progress, step, error)
            VALUES (@book, 'source', @status, @progress::real, @step, @error) RETURNING id
            """,
            new { book = SeedIds.Books.ProductEngineering, status, progress, step, error });
    }
}
