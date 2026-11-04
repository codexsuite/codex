using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J11 · Triage unflagged quotes and new uses, as the agent does it (#16 J11, #20 get_clearance, cite_source,
// record_clearance_decision, update_source): the inbox holds f1 and f2; f1 is not a quote and is never raised again; f2 is
// cited from the flag, which takes The Velocity Trap to 376 of 300 words, still a risk; Accelerate's new use is reviewed and
// signed off again; DORA's details are set; the Agile Manifesto is accepted as fair use and signed off. The HTTP API must
// agree, and the inbox ends empty.
// Deviation (Joel, 2026-10-22, M6): steps 4 and 6's "Sign off" and "Accept as fair use" are the author's clicks in References,
// never an agent's call. The agent prepares each (record_clearance_decision comes back blocked with the summary and the
// open_url, nothing is written) and the test makes the call through the References endpoint the button uses, as the author.
// Deviations: step 5's re-check and accept-the-redirect go to the network, so they run in McpUpdateSourceRecheckTests over a
// fake lookup, and here DORA's licence, rights holder and Accessed are set without them; a not_a_quote note is not stored
// (no References command takes one), so the HTTP API shows the flag as dismissed with no note.
[TestFixture]
[Category("J11")]
[CancelAfter(120000)]
public class J11TriageUnflaggedQuotesTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<int> OpenFlagsAsync() =>
        (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/quote-flags")).GetArrayLength();

    [Test]
    [Order(1)]
    public async Task GetClearance_WhenTheInboxIsOpened_ShouldListTwoFlagsWithWhereTheyAre()
    {
        var body = await Client.OkAsync("get_clearance", new { book = Book });

        body.GetProperty("summary").GetProperty("unflagged_quotes").GetInt32().ShouldBe(2);
        var flags = body.GetProperty("flags").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetGuid());
        flags[SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote].GetProperty("scene").GetProperty("number").GetString().ShouldBe("6.1");
        flags[SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote].GetProperty("excerpt").GetString().ShouldBe("The customer is not in the building");
        flags[SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch].GetProperty("scene").GetProperty("number").GetString().ShouldBe("8.1");
        flags[SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch].GetProperty("source_guess").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.VelocityTrap);
        flags[SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch].GetProperty("match_percent").GetInt32().ShouldBe(93);
        flags[SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch].GetProperty("locator").GetString().ShouldBe("p. 112");
        (await OpenFlagsAsync()).ShouldBe(2);
    }

    [Test]
    [Order(2)]
    public async Task RecordClearanceDecision_WhenF1IsTheFacilitatorsOwnWords_ShouldDismissItForGood()
    {
        var body = await Client.OkAsync("record_clearance_decision",
            new { decision = "not_a_quote", flag = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote, note = "The facilitator's own words." });

        body.GetProperty("open_flags").GetInt32().ShouldBe(1);
        (await _server.ScalarAsync<string>("SELECT status FROM quote_flag WHERE id = @id", new { id = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote })).ShouldBe("dismissed");
        (await OpenFlagsAsync()).ShouldBe(1);

        // "Never raised again": the scan that runs when the scene is saved again does not raise f1 a second time.
        await _server.ScanAsync(SeedIds.Books.ProductEngineering, SeedIds.Scenes.Ch6Sc1);
        var open = (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/quote-flags")).EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).ToList();
        open.ShouldBe([SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch]);
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM quote_flag WHERE scene_id = @id AND status = 'open'", new { id = SeedIds.Scenes.Ch6Sc1 })).ShouldBe(0);
    }

    [Test]
    [Order(3)]
    public async Task CiteSource_WhenF2IsCitedFromTheFlag_ShouldAdd34WordsAndStayARiskAt376()
    {
        var body = await Client.OkAsync("cite_source",
            new { from_flag = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch, kind = "quote", locator = "p. 112", source = "#4", book = Book });

        body.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.VelocityTrap);
        body.GetProperty("status").GetString().ShouldBe("risk");
        body.GetProperty("reason").GetString().ShouldBe("376 of 300 words, no request");
        body.GetProperty("scene").GetProperty("number").GetString().ShouldBe("8.1");
        (await OpenFlagsAsync()).ShouldBe(0);
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("quotedWords").GetInt32().ShouldBe(376);
        http.GetProperty("uses").EnumerateArray().Any(u => u.GetProperty("locator").GetString() == "p. 112").ShouldBeTrue();
    }

    [Test]
    [Order(4)]
    public async Task RecordClearanceDecision_WhenAcceleratesNewUseIsAFairParaphrase_ShouldBePreparedByTheAgentAndSignedOffByTheAuthor()
    {
        var needsCheck = await Client.OkAsync("get_clearance", new { book = Book, status = new[] { "needs_check" }, include = Array.Empty<string>() });
        needsCheck.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("title").GetString() == "Accelerate")
            .GetProperty("next_action").GetString().ShouldBe("review_new_use");
        var detail = await Client.OkAsync("get_clearance", new { book = Book, source = "#8", include = new[] { "uses" } });
        var fresh = detail.GetProperty("detail").GetProperty("uses").EnumerateArray().Where(u => u.GetProperty("new_since_sign_off").GetBoolean()).ToList();
        fresh.ShouldHaveSingleItem().GetProperty("scene").GetProperty("number").GetString().ShouldStartWith("5.");

        // The agent prepares the sign-off; the author makes it in References (the button's endpoint).
        var events = await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE reference_id = @id", new { id = SeedIds.References.Accelerate });
        var error = await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", source = "#8", book = Book });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("author_decision");
        error.GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.Accelerate}");
        (await _server.ApiAsync($"/api/references/{SeedIds.References.Accelerate}")).GetProperty("verdict").GetProperty("status").GetString().ShouldBe("needs_check");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE reference_id = @id", new { id = SeedIds.References.Accelerate })).ShouldBe(events);

        await _server.PostApiAsync($"/api/references/{SeedIds.References.Accelerate}/sign-off");

        (await _server.ApiAsync($"/api/references/{SeedIds.References.Accelerate}")).GetProperty("verdict").GetProperty("status").GetString().ShouldBe("cleared");
    }

    [Test]
    [Order(5)]
    public async Task UpdateSource_WhenDoraGetsItsLicenceRightsHolderAndAccessedDate_ShouldStopAskingForALicence()
    {
        var body = await Client.OkAsync("update_source", new
        {
            source = "#7", book = Book, fields = new { licence = "all_rights_reserved", rights_holder = "DORA", accessed_on = "2026-10-19" },
        });

        body.GetProperty("reason").GetString().ShouldBe("An ISBN, DOI or URL does not check out");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.DoraReport2024}");
        http.GetProperty("details").GetProperty("licence").GetString().ShouldBe("all_rights_reserved");
        http.GetProperty("details").GetProperty("rightsHolder").GetString().ShouldBe("DORA");
        http.GetProperty("details").GetProperty("accessedOn").GetString().ShouldBe("2026-10-19");
    }

    [Test]
    [Order(6)]
    public async Task RecordClearanceDecision_WhenTheManifestoIsAcceptedAsFairUse_ShouldBePreparedByTheAgentAndDecidedByTheAuthor()
    {
        var error = await Client.ErrorAsync("record_clearance_decision",
            new { decision = "accept_fair_use", source = "#2", book = Book, note = "A seven-word quote, to criticise it." });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("author_decision");
        error.GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.AgileManifestoWeb}");
        (await _server.ScalarAsync<bool>("SELECT fair_use_decided_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.AgileManifestoWeb })).ShouldBeTrue();

        // The author's clicks: Accept as fair use, then Sign off.
        var fairUse = await _server.PostApiAsync($"/api/references/{SeedIds.References.AgileManifestoWeb}/fair-use", new { reason = "A seven-word quote, to criticise it." });
        fairUse.GetProperty("verdict").GetProperty("reason").GetString().ShouldBe("ready_to_sign_off");
        var signed = await _server.PostApiAsync($"/api/references/{SeedIds.References.AgileManifestoWeb}/sign-off");

        signed.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("cleared");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.AgileManifestoWeb}");
        http.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("cleared");
        http.GetProperty("details").GetProperty("fairUseNote").GetString().ShouldBe("A seven-word quote, to criticise it.");
    }

    [Test]
    [Order(7)]
    public async Task GetClearance_WhenTheTriageIsDone_ShouldShowAnEmptyInboxAndNothingLeftToCheckButDora()
    {
        var body = await Client.OkAsync("get_clearance", new { book = Book });

        body.GetProperty("summary").GetProperty("unflagged_quotes").GetInt32().ShouldBe(0);
        body.GetProperty("flags").GetArrayLength().ShouldBe(0);
        var left = body.GetProperty("sources").EnumerateArray().Where(s => s.GetProperty("status").GetString() == "needs_check")
            .Select(s => s.GetProperty("title").GetString()).ToList();
        left.ShouldNotContain("Accelerate");
        left.ShouldNotContain("Manifesto for Agile Software Development");
        left.ShouldContain(s => s!.Contains("DORA", StringComparison.Ordinal) || s.Contains("State of DevOps", StringComparison.Ordinal));
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE source = 'agent'")).ShouldBe(1);
    }
}
