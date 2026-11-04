using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J1 · Pick up where I left off, as the agent does it (#16 J1, #20 resume_work, get_draft, resolve_draft, edit_outline):
// resume_work names Ch 5 · Sc 2 with its version and the pending draft's text; the agent applies the draft and swaps the
// label. A stale version is refused and the HTTP API sees the applied prose.
[TestFixture]
[Category("J1")]
[CancelAfter(120000)]
public class J1PickUpTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;
    private int _version;
    private string _draftText = "";
    private string _undo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ResumeWork_WhenTheAgentStartsAFreshSession_ShouldNameTheSceneItsVersionAndThePendingDraft()
    {
        var body = await Client.OkAsync("resume_work");

        body.GetProperty("book").GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.ProductEngineering);
        var last = body.GetProperty("last_scene");
        last.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch5Sc2);
        _version = last.GetProperty("version").GetInt32();
        var draft = last.GetProperty("pending_draft");
        draft.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.AiGenerations.Ch5Sc2Pending);
        draft.GetProperty("attempt").GetInt32().ShouldBe(1);
        _draftText = draft.GetProperty("text").GetString()!;
        _draftText.ShouldNotBeEmpty();
        (await _server.ProseAsync(SeedIds.Scenes.Ch5Sc2)).Version.ShouldBe(_version);
    }

    [Test]
    [Order(2)]
    public async Task ResolveDraft_WhenApplyingWithAStaleVersion_ShouldConflictAndLeaveTheSceneAlone()
    {
        var before = await _server.ProseAsync(SeedIds.Scenes.Ch5Sc2);

        var error = await Client.ErrorAsync("resolve_draft",
            new { generation = SeedIds.AiGenerations.Ch5Sc2Pending, action = "apply", expected_version = _version + 5 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await _server.ProseAsync(SeedIds.Scenes.Ch5Sc2)).ShouldBe(before);
    }

    [Test]
    [Order(3)]
    public async Task ResolveDraft_WhenApplyingWithTheResumeVersion_ShouldReturnTheEnvelopeAndWriteTheProse()
    {
        var result = await Client.OkAsync("resolve_draft",
            new { generation = SeedIds.AiGenerations.Ch5Sc2Pending, action = "apply", expected_version = _version });

        _undo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("version").GetInt32().ShouldBe(_version + 1);
        result.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch5Sc2);
        result.GetProperty("affected").GetArrayLength().ShouldBeGreaterThan(0);
        result.GetProperty("side_effects").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        (await _server.ProseAsync(SeedIds.Scenes.Ch5Sc2)).Version.ShouldBe(_version + 1);
    }

    [Test]
    [Order(4)]
    public async Task ResolveDraft_WhenApplied_ShouldBeVisibleThroughTheHttpApiAsProseAndAnAiApplyRevision()
    {
        var prose = await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch5Sc2}/prose");
        prose.GetProperty("version").GetInt32().ShouldBe(_version + 1);
        prose.GetProperty("body").GetString()!.ShouldContain(_draftText.Split('\n')[0].Trim());
        (await _server.ScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = SeedIds.AiGenerations.Ch5Sc2Pending }))
            .ShouldBe("applied");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @id AND source = 'ai_apply'", new { id = SeedIds.Scenes.Ch5Sc2 }))
            .ShouldBeGreaterThan(0);
    }

    [Test]
    [Order(5)]
    public async Task EditOutline_WhenSwappingTheLabel_ShouldShowDraftingOnThePlanCard()
    {
        var result = await Client.OkAsync("edit_outline", new
        {
            book = ProductEngineering,
            ops = new[] { new { op = "update", item = "5.2", add_labels = new[] { "Drafting" }, remove_labels = new[] { "Needs research" } } },
        });

        result.GetProperty("results").GetArrayLength().ShouldBe(1);
        var outline = await _server.ApiAsync($"/api/books/{ProductEngineering}/outline");
        var scene = outline.GetProperty("acts").EnumerateArray()
            .SelectMany(a => a.GetProperty("chapters").EnumerateArray())
            .SelectMany(c => c.GetProperty("scenes").EnumerateArray())
            .Single(s => s.GetProperty("number").GetString() == "5.2");
        scene.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()).ShouldBe(["Drafting"]);
        scene.GetProperty("words").GetInt32().ShouldBeGreaterThan(0);
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenTheApplyIsUndone_ShouldRestoreTheDraftToPending()
    {
        await Client.OkAsync("undo", new { undo_id = _undo });

        (await _server.ScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = SeedIds.AiGenerations.Ch5Sc2Pending }))
            .ShouldBe("pending");
    }
}
