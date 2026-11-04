using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Fakes;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// draft_prose, submit_draft and resolve_draft on Ch 4 · Sc 1, against the in-process server whose generator is the
// fake (McpInProcessServer). The first draft is held after its first chunk so the test sees the job running before
// its text exists. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(90000)]
public class McpDraftTests
{
    private const string Chunk1 = "The fog came in. ";
    private const string Chunk2 = "Nobody noticed.";
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private McpInProcessServer _server = null!;
    private string _generation = "";
    private string _draftUndo = "";
    private string _applyUndo = "";
    private string _submitted = "";
    private string _bodyBefore = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpInProcessServer.StartAsync(new FakeProseGenerator([Chunk1, Chunk2], _gate.Task));
        _bodyBefore = (await ProseAsync()).Body;
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        _gate.TrySetResult();
        await _server.DisposeAsync();
    }

    [Test]
    [Order(1)]
    public async Task DraftProse_WhenCalled_ShouldReturnTheGenerationAndAJobBeforeTheTextExists()
    {
        var result = await Client.OkAsync("draft_prose", new { scene = Scene });

        _generation = result.GetProperty("generation").GetString()!;
        result.GetProperty("job_id").GetString().ShouldBe(_generation);
        result.GetProperty("state").GetString().ShouldBe("running");
        result.GetProperty("generations_left").GetInt32().ShouldBe(19);
        _draftUndo = result.GetProperty("undo_id").GetString()!;
        var job = await Client.OkAsync("get_job", new { job_id = _generation });
        job.GetProperty("state").GetString().ShouldBe("running");
        job.GetProperty("kind").GetString().ShouldBe("draft");
        (await ProseAsync()).Body.ShouldBe(_bodyBefore);
    }

    [Test]
    [Order(2)]
    public async Task DraftProse_WhenTheModelFinishes_ShouldMakeTheTextReadableAndNeverApplyIt()
    {
        _gate.SetResult();
        var job = await Client.OkAsync("get_job", new { job_id = _generation });
        while (job.GetProperty("state").GetString() == "running")
        {
            job = await Client.OkAsync("get_job", new { job_id = _generation });
        }

        job.GetProperty("state").GetString().ShouldBe("done");
        job.GetProperty("draft_status").GetString().ShouldBe("pending");
        var draft = (await Client.OkAsync("get_draft", new { draft = _generation })).GetProperty("draft");
        draft.GetProperty("text").GetString().ShouldBe(Chunk1 + Chunk2);
        draft.GetProperty("model").GetString().ShouldBe("claude-sonnet-5-5");
        (await ProseAsync()).Body.ShouldBe(_bodyBefore);
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QuerySingleAsync<(string, string)>(
            "SELECT created_via, client_name FROM ai_generation WHERE id = @id", new { id = Guid.Parse(_generation) }))
            .ShouldBe(("mcp", "test-agent"));
    }

    [Test]
    [Order(3)]
    public async Task ResolveDraft_WhenApplyingWithAStaleVersion_ShouldConflictAndWriteNothing()
    {
        var version = (await ProseAsync()).Version;

        var error = await Client.ErrorAsync("resolve_draft", new { generation = _generation, action = "apply", expected_version = version - 1 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        error.GetProperty("message").GetString()!.ShouldContain($"version {version}");
        (await ProseAsync()).ShouldBe((_bodyBefore, version));
    }

    [Test]
    [Order(4)]
    public async Task ResolveDraft_WhenApplyingWithoutAVersion_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("resolve_draft", new { generation = _generation, action = "apply" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("expected_version");
    }

    [Test]
    [Order(5)]
    public async Task ResolveDraft_WhenApplyingWithTheCurrentVersion_ShouldAppendTheDraft()
    {
        var version = (await ProseAsync()).Version;

        var result = await Client.OkAsync("resolve_draft", new { generation = _generation, action = "apply", expected_version = version });

        _applyUndo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("version").GetInt32().ShouldBe(version + 1);
        var (body, now) = await ProseAsync();
        now.ShouldBe(version + 1);
        body.ShouldEndWith(Chunk1.TrimEnd() + " " + Chunk2 + "\n");
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenTheDraftWasApplied_ShouldPutTheSceneBackAndTheDraftWaiting()
    {
        await Client.OkAsync("undo", new { undo_id = _applyUndo });

        (await ProseAsync()).Body.ShouldBe(_bodyBefore);
        var draft = (await Client.OkAsync("get_draft", new { draft = _generation })).GetProperty("draft");
        draft.GetProperty("status").GetString().ShouldBe("pending");
    }

    [Test]
    [Order(7)]
    public async Task SubmitDraft_WhenTheTextInventsABeat_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("submit_draft", new { scene = Scene, text = $"Some words.\n\n::beat{{id=\"{Guid.CreateVersion7()}\"}}\n" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("text");
    }

    [Test]
    [Order(8)]
    public async Task SubmitDraft_WhenTheAgentWroteIt_ShouldStoreItWithoutAModelLabel()
    {
        var result = await Client.OkAsync("submit_draft", new { scene = Scene, text = "The agent wrote this." });

        _submitted = result.GetProperty("generation").GetString()!;
        var draft = (await Client.OkAsync("get_draft", new { draft = _submitted })).GetProperty("draft");
        draft.GetProperty("model").GetString().ShouldBe("client");
        draft.GetProperty("status").GetString().ShouldBe("pending");
        draft.GetProperty("text").GetString().ShouldBe("The agent wrote this.");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QuerySingleAsync<(string, string)>(
            "SELECT created_via, client_name FROM ai_generation WHERE id = @id", new { id = Guid.Parse(_submitted) }))
            .ShouldBe(("mcp", "test-agent"));
    }

    [Test]
    [Order(9)]
    public async Task ResolveDraft_WhenApplyingADraftTheAgentSubmitted_ShouldAppendItAndNameNoModel()
    {
        var version = (await ProseAsync()).Version;

        await Client.OkAsync("resolve_draft", new { generation = _submitted, action = "apply", expected_version = version });

        (await ProseAsync()).Body.ShouldEndWith("The agent wrote this.\n");
    }

    [Test]
    [Order(10)]
    public async Task ResolveDraft_WhenDiscardingADraftThatWasApplied_ShouldConflict()
    {
        var error = await Client.ErrorAsync("resolve_draft", new { generation = _submitted, action = "discard" });

        error.GetProperty("code").GetString().ShouldBe("conflict");
    }

    [Test]
    [Order(11)]
    public async Task Undo_WhenTheDraftStartedByDraftProseIsUndone_ShouldDiscardIt()
    {
        await Client.OkAsync("undo", new { undo_id = _draftUndo });

        var draft = (await Client.OkAsync("get_draft", new { draft = _generation })).GetProperty("draft");
        draft.GetProperty("status").GetString().ShouldBe("discarded");
    }

    [Test]
    [Order(12)]
    public async Task GetJob_WhenTheIdIsNotAJob_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("get_job", new { job_id = Guid.CreateVersion7() });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId = SeedIds.Scenes.Ch4Sc1 });
    }
}
