using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// record_clearance_decision over stdio, on the Product Engineering seed. Copyright decisions are UI-only over MCP (M6, Joel
// 2026-10-22): sign_off and accept_fair_use never write, they come back blocked (rule author_decision) with the summary of
// uses and words and the open_url where the author decides; a source that is still a risk is refused in the panel's words
// first. reopen and not_a_quote still work, with provenance 'agent' written at insert time. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(120000)]
public class McpClearanceDecisionTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<T?> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args);
    }

    private static string ReferencesUrl(Guid source) => $"/books/{SeedIds.Books.ProductEngineering}/references#{source}";

    [Test]
    [Order(1)]
    public async Task RecordClearanceDecision_WhenSigningOffARisk_ShouldBeBlockedInThePanelsWordsWithTheReferencesLink()
    {
        var error = await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", source = "#4", book = Book });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("message").GetString()!.ShouldContain("Quoted prose is over the limit: 342 of 300 words.");
        error.GetProperty("rule").GetString().ShouldBe("copyright_risk");
        error.GetProperty("open_url").GetString().ShouldBe(ReferencesUrl(SeedIds.References.VelocityTrap));
    }

    [Test]
    [Order(2)]
    public async Task RecordClearanceDecision_WhenSigningOffASourceThatIsReady_ShouldBeBlockedAsTheAuthorsCallAndWriteNothing()
    {
        var changeSets = await ScalarAsync<int>("SELECT count(*)::int FROM change_set");
        var events = await ScalarAsync<int>("SELECT count(*)::int FROM reference_event");

        var error = await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", source = "#3", book = Book, confirm = true });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("author_decision");
        error.GetProperty("open_url").GetString().ShouldBe(ReferencesUrl(SeedIds.References.GoodhartMonetary));
        error.GetProperty("message").GetString()!.ShouldContain("quoted words");
        (await ScalarAsync<bool>("SELECT signed_off_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.GoodhartMonetary })).ShouldBeTrue();
        (await ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(changeSets);
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_event")).ShouldBe(events);
    }

    [Test]
    [Order(3)]
    public async Task RecordClearanceDecision_WhenFairUseIsAccepted_ShouldBeBlockedAsTheAuthorsCallAndWriteNothing()
    {
        var events = await ScalarAsync<int>("SELECT count(*)::int FROM reference_event");

        var error = await Client.ErrorAsync("record_clearance_decision",
            new { decision = "accept_fair_use", source = "#2", book = Book, note = "Seven words of a manifesto, quoted to criticise it." });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("author_decision");
        error.GetProperty("open_url").GetString().ShouldBe(ReferencesUrl(SeedIds.References.AgileManifestoWeb));
        error.GetProperty("message").GetString()!.ShouldContain("Seven words of a manifesto");
        (await ScalarAsync<bool>("SELECT fair_use_decided_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.AgileManifestoWeb })).ShouldBeTrue();
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_event")).ShouldBe(events);
    }

    [Test]
    [Order(4)]
    public async Task RecordClearanceDecision_WhenReopeningAClearedSource_ShouldWriteTheAgentsProvenanceAndUndoShouldNotSignItOffAgain()
    {
        var body = await Client.OkAsync("record_clearance_decision", new { decision = "reopen", source = "#1", book = Book });

        body.GetProperty("status_before").GetString().ShouldBe("cleared");
        body.GetProperty("status").GetString().ShouldBe("needs_check");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'reopened'", new { id = SeedIds.References.BeckXp })).ShouldBe("agent");

        var undo = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        undo.GetProperty("code").GetString().ShouldBe("blocked");
        undo.GetProperty("message").GetString()!.ShouldContain("a sign-off is the author's call");
        (await ScalarAsync<bool>("SELECT signed_off_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.BeckXp })).ShouldBeTrue();
    }

    [Test]
    [Order(5)]
    public async Task RecordClearanceDecision_WhenAFlagIsNotAQuote_ShouldDismissItForGood()
    {
        var body = await Client.OkAsync("record_clearance_decision",
            new { decision = "not_a_quote", flag = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote, note = "The facilitator's own words." });

        body.GetProperty("status").GetString().ShouldBe("dismissed");
        body.GetProperty("open_flags").GetInt32().ShouldBe(1);
        (await ScalarAsync<string>("SELECT status FROM quote_flag WHERE id = @id", new { id = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote })).ShouldBe("dismissed");
        (await Client.OkAsync("get_clearance", new { book = Book })).GetProperty("summary").GetProperty("unflagged_quotes").GetInt32().ShouldBe(1);
        (await Client.ErrorAsync("record_clearance_decision", new { decision = "not_a_quote", flag = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote }))
            .GetProperty("code").GetString().ShouldBe("conflict");
        (await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() })).GetProperty("code").GetString().ShouldBe("blocked");
    }

    [Test]
    [Order(6)]
    public async Task RecordClearanceDecision_WhenTheArgumentsDoNotFit_ShouldBeInvalid()
    {
        (await Client.ErrorAsync("record_clearance_decision", new { decision = "approve", source = "#8", book = Book })).GetProperty("field").GetString().ShouldBe("decision");
        (await Client.ErrorAsync("record_clearance_decision", new { decision = "accept_fair_use", source = "#8", book = Book })).GetProperty("field").GetString().ShouldBe("note");
        (await Client.ErrorAsync("record_clearance_decision", new { decision = "not_a_quote", source = "#8", book = Book })).GetProperty("field").GetString().ShouldBe("flag");
        (await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", flag = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote })).GetProperty("field").GetString().ShouldBe("source");
        (await Client.ErrorAsync("record_clearance_decision", new { decision = "sign_off", source = "#99", book = Book })).GetProperty("code").GetString().ShouldBe("not_found");
    }
}
