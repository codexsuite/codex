using Codex.Contracts;
using Codex.Core;
using Codex.Mcp.Tests.Support;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J10 · Clear a copyright risk, path 1 (paraphrase), as the agent does it (#16 J10, #20 get_clearance, submit_draft,
// resolve_draft, update_source_use, record_clearance_decision): The Velocity Trap is at 342 of 300 words, so signing it off
// is blocked; the agent rewrites the Ch 8 words (a draft, Apply) and rewraps the same use onto them as a paraphrase
// (update_source_use, M1), which makes it 214 of 300, Needs check and Ready to sign off; the agent prepares the sign-off and
// the author signs it off in References, which then shows it Cleared. The HTTP API the References tab reads must agree at
// each step.
// Deviation (Joel, 2026-10-22, M6): step 5's "Sign off" is the author's click in References, never an agent's call. The agent
// prepares it (record_clearance_decision sign_off comes back blocked with the summary and the open_url) and the test signs off
// through the References command as the author, the way the button does.
[TestFixture]
[Category("J10")]
[CancelAfter(120000)]
public class J10ClearACopyrightRiskTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;
    private const string Paraphrase = "Teams that cannot finish on time do not get faster by growing, because the newcomers have to be taught while the rest slow down.";
    private string _rewrapUndo = "";
    private int _version;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task GetClearance_WhenTheRiskIsLookedUp_ShouldNameTheWordsAndTheNextAction()
    {
        var body = await Client.OkAsync("get_clearance", new { book = Book, source = "#4" });

        var focus = body.GetProperty("focus");
        focus.GetProperty("status").GetString().ShouldBe("risk");
        focus.GetProperty("reason").GetString().ShouldBe("342 of 300 words, no request");
        focus.GetProperty("next_action").GetString().ShouldBe("reduce_quoted_words");
        var uses = body.GetProperty("detail").GetProperty("uses").EnumerateArray().ToList();
        uses.Select(u => u.GetProperty("scene").GetProperty("number").GetString()).ShouldBe(["4.1", "8.1"]);
        uses.Select(u => u.GetProperty("id").GetGuid()).ShouldContain(SeedIds.ReferenceUses.VelocityTrapCh8Sc1);
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("risk");
        http.GetProperty("quotedWords").GetInt32().ShouldBe(342);
    }

    [Test]
    [Order(2)]
    public async Task RecordClearanceDecision_WhenSigningOffTheRisk_ShouldBeBlockedInThePanelsWords()
    {
        var error = await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", source = "#4", book = Book });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("message").GetString()!.ShouldContain("342 of 300 words");
    }

    [Test]
    [Order(3)]
    public async Task ResolveDraft_WhenTheParaphraseDraftIsApplied_ShouldAddTheNewWordsToTheScene()
    {
        var draft = await Client.OkAsync("submit_draft", new { scene = SeedIds.Scenes.Ch8Sc1, text = Paraphrase });
        var before = await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/prose");

        var result = await Client.OkAsync("resolve_draft",
            new { generation = draft.GetProperty("generation").GetString(), action = "apply", expected_version = before.GetProperty("version").GetInt32() });

        _version = result.GetProperty("version").GetInt32();
        var after = await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/prose");
        after.GetProperty("body").GetString()!.ShouldContain(Paraphrase);
        after.GetProperty("version").GetInt32().ShouldBe(_version);
    }

    [Test]
    [Order(4)]
    public async Task UpdateSourceUse_WhenTheCh8UseIsRewrappedOntoTheParaphrase_ShouldReadNeedsCheckAndReadyToSignOff()
    {
        var body = await Client.OkAsync("update_source_use",
            new { use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1, rewrap = new { text = Paraphrase }, kind = "paraphrase", expected_version = _version });

        body.GetProperty("status").GetString().ShouldBe("needs_check");
        body.GetProperty("reason").GetString().ShouldBe("214 of 300 words. Ready to sign off");
        body.GetProperty("quoted_words").GetInt32().ShouldBe(214);
        body.GetProperty("affected").EnumerateArray().Select(a => a.GetProperty("field").GetString()).ShouldContain("status");
        _rewrapUndo = body.GetProperty("undo_id").GetString()!;
        var prose = await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/prose");
        prose.GetProperty("body").GetString()!.ShouldContain($"[{Paraphrase}](ref:{SeedIds.ReferenceUses.VelocityTrapCh8Sc1})");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("verdict").GetProperty("reason").GetString().ShouldBe("ready_to_sign_off");
        http.GetProperty("quotedWords").GetInt32().ShouldBe(214);
    }

    [Test]
    [Order(5)]
    public async Task RecordClearanceDecision_WhenTheAgentPreparesTheSignOff_ShouldNameTheWordsAndWriteNothingUntilTheAuthorSignsOff()
    {
        var events = await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap });

        var error = await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", source = "#4", book = Book, note = "Rewritten in Ch 8." });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("author_decision");
        error.GetProperty("message").GetString()!.ShouldContain("214 of 300 quoted words");
        error.GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.VelocityTrap}");
        var pending = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        pending.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("needs_check");
        pending.GetProperty("verdict").GetProperty("signedOffBy").ValueKind.ShouldBe(JsonValueKind.Null);
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBe(events);

        // Step 5, the author's click: the References command the Sign off button calls.
        var signed = await _server.PostApiAsync($"/api/references/{SeedIds.References.VelocityTrap}/sign-off");

        signed.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("cleared");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("cleared");
        var signedAt = await _server.ScalarAsync<DateTimeOffset>("SELECT signed_off_at FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap });
        http.GetProperty("verdict").GetProperty("signedOffBy").GetString()
            .ShouldBe(await _server.ScalarAsync<string>("SELECT u.display_name FROM reference r JOIN app_user u ON u.id = r.signed_off_by WHERE r.id = @id", new { id = SeedIds.References.VelocityTrap }));
        http.GetProperty("verdict").GetProperty("signedOffLabel").GetString().ShouldBe(DisplayDate.Relative(signedAt, TimeProvider.System));
        (await _server.ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'signed_off'", new { id = SeedIds.References.VelocityTrap }))
            .ShouldBe("user");
        var summary = (await Client.OkAsync("get_clearance", new { book = Book })).GetProperty("summary");
        summary.GetProperty("risks").GetInt32().ShouldBe(2);
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenTheRewrapIsUndoneAfterTheSignOff_ShouldPutTheUseBackAndTheSourceNeedsCheckAgain()
    {
        await Client.OkAsync("undo", new { undo_id = _rewrapUndo });

        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("quotedWords").GetInt32().ShouldBe(342);
        http.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("risk");
    }
}
