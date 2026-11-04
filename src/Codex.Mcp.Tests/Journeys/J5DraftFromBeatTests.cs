using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J5 · Draft a scene from a beat, as the agent does it (#16 J5, #20 upsert_beat, submit_draft, resolve_draft, edit_prose) on
// Ch 3 · Sc 3 "Bad Scrum". The stdio process has no model key, so the agent offers its own two attempts with submit_draft
// (generation itself is covered in-process by McpDraftTests): set length 600 and add Agile Manifesto as context, offer attempt 1,
// discard it, offer attempt 2, apply it, then hand-edit the applied prose. Stale versions are refused.
[TestFixture]
[Category("J5")]
[CancelAfter(120000)]
public class J5DraftFromBeatTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Scene = SeedIds.Scenes.Ch3Sc3.ToString();
    private const string Attempt1 = "A first try at bad scrum.";
    private const string Attempt2 = "Standups that report to a manager, a board nobody trusts.\n\nReviews held for show.";
    private JourneyServer _server = null!;
    private string _first = "";
    private string _second = "";
    private int _version;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await JourneyServer.StartAsync();
        _version = (await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3)).Version;
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task UpsertBeat_WhenSettingLengthAndContext_ShouldUpdateTheBeatAndItsContextEntries()
    {
        var result = await Client.OkAsync("upsert_beat",
            new { beat = SeedIds.Beats.Ch3Sc3BadScrum, book = Book, target_word_count = 600, context_add = new[] { "Agile Manifesto (2001)" } });

        result.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        result.GetProperty("affected").GetArrayLength().ShouldBeGreaterThan(0);
        (await _server.ScalarAsync<int>("SELECT target_word_count FROM beat WHERE id = @id", new { id = SeedIds.Beats.Ch3Sc3BadScrum })).ShouldBe(600);
        (await _server.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM beat_kb WHERE beat_id = @id AND kb_entry_id = @entry AND archived_at IS NULL)",
            new { id = SeedIds.Beats.Ch3Sc3BadScrum, entry = SeedIds.KbEntries.AgileManifesto })).ShouldBeTrue();
    }

    [Test]
    [Order(2)]
    public async Task UpsertBeat_WhenTheLengthIsZero_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("upsert_beat", new { beat = SeedIds.Beats.Ch3Sc3BadScrum, target_word_count = 0 });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        (await _server.ScalarAsync<int>("SELECT target_word_count FROM beat WHERE id = @id", new { id = SeedIds.Beats.Ch3Sc3BadScrum })).ShouldBe(600);
    }

    [Test]
    [Order(3)]
    public async Task SubmitDraft_WhenOfferingTwoAttempts_ShouldStoreBothPendingAndApplyNeither()
    {
        var before = await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3);
        var one = await Client.OkAsync("submit_draft", new { beat = SeedIds.Beats.Ch3Sc3BadScrum, text = Attempt1 });
        var two = await Client.OkAsync("submit_draft", new { beat = SeedIds.Beats.Ch3Sc3BadScrum, text = Attempt2 });

        _first = one.GetProperty("generation").GetString()!;
        _second = two.GetProperty("generation").GetString()!;
        two.GetProperty("status").GetString().ShouldBe("pending");
        two.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        two.GetProperty("side_effects").GetArrayLength().ShouldBeGreaterThan(0);
        (await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3)).ShouldBe(before);
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM ai_generation WHERE beat_id = @id AND status = 'pending'", new { id = SeedIds.Beats.Ch3Sc3BadScrum }))
            .ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    [Order(4)]
    public async Task SubmitDraft_WhenTheTextCarriesABeatLine_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("submit_draft", new { beat = SeedIds.Beats.Ch3Sc3BadScrum, text = $"Text.\n\n::beat{{id=\"{Guid.CreateVersion7()}\"}}\n" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(5)]
    public async Task ResolveDraft_WhenDiscardingAttempt1_ShouldMarkItDiscardedAndLeaveTheProse()
    {
        var before = await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3);

        var result = await Client.OkAsync("resolve_draft", new { generation = _first, action = "discard" });

        result.GetProperty("action").GetString().ShouldBe("discard");
        (await _server.ScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = Guid.Parse(_first) })).ShouldBe("discarded");
        (await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3)).ShouldBe(before);
    }

    [Test]
    [Order(6)]
    public async Task ResolveDraft_WhenApplyingAttempt2WithAStaleVersion_ShouldConflict()
    {
        var error = await Client.ErrorAsync("resolve_draft", new { generation = _second, action = "apply", expected_version = _version + 3 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
    }

    [Test]
    [Order(7)]
    public async Task ResolveDraft_WhenApplyingAttempt2_ShouldPutItsProseUnderTheBeat()
    {
        var result = await Client.OkAsync("resolve_draft", new { generation = _second, action = "apply", expected_version = _version });

        result.GetProperty("action").GetString().ShouldBe("apply");
        result.GetProperty("version").GetInt32().ShouldBe(_version + 1);
        var prose = await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch3Sc3}/prose");
        prose.GetProperty("version").GetInt32().ShouldBe(_version + 1);
        var body = prose.GetProperty("body").GetString()!;
        body.ShouldContain("::beat");
        body.ShouldContain("Standups that report to a manager");
        body.ShouldNotContain(Attempt1);
        (await _server.ScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = Guid.Parse(_second) })).ShouldBe("applied");
    }

    [Test]
    [Order(8)]
    public async Task EditProse_WhenTheAgentEditsTheAppliedProseByHand_ShouldBePlainTextAndBumpTheVersion()
    {
        var version = (await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3)).Version;

        var result = await Client.OkAsync("edit_prose",
            new { scene = Scene, expected_version = version, edits = new[] { new { old = "Reviews held for show.", @new = "Reviews held for the room." } } });

        result.GetProperty("version").GetInt32().ShouldBe(version + 1);
        (await _server.ProseAsync(SeedIds.Scenes.Ch3Sc3)).Body.ShouldContain("Reviews held for the room.");
    }
}
