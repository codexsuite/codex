using Codex.Contracts;
using Codex.Contracts.Sources;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.Mcp.Tests;

// update_source's recheck and accept_redirect on the DORA source (J11 step 5), in the test process so ISourceLookup is the
// fake one (the stdio process would go to the network): the re-check reports the moved address, accept_redirect makes it the
// source's URL, and undo puts the old address back. Accepting with nothing moved is invalid.
[TestFixture]
[CancelAfter(120000)]
public class McpUpdateSourceRecheckTests
{
    private const string Moved = "https://dora.dev/research/2024/dora-report/";
    private McpInProcessServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() =>
        _server = await McpInProcessServer.StartAsync(new FakeProseGenerator(["Words."]), configure: services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup()));

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<string?> UrlAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT url FROM reference WHERE id = @id", new { id = SeedIds.References.DoraReport2024 });
    }

    [Test]
    [Order(1)]
    public async Task UpdateSource_WhenTheUrlIsRechecked_ShouldReportWhereItNowLands()
    {
        var body = await _server.Client.OkAsync("update_source", new { source = SeedIds.References.DoraReport2024.ToString(), recheck = new[] { "url" } });

        var check = body.GetProperty("rechecks").EnumerateArray().Single();
        check.GetProperty("field").GetString().ShouldBe("url");
        check.GetProperty("outcome").GetString().ShouldBe("redirect");
        check.GetProperty("redirect_to").GetString().ShouldBe(Moved);
        (await UrlAsync()).ShouldBe("https://dora.dev/research/2024/");
    }

    [Test]
    [Order(2)]
    public async Task UpdateSource_WhenTheRedirectIsAccepted_ShouldReplaceTheUrlAndUndoShouldPutItBack()
    {
        var body = await _server.Client.OkAsync("update_source", new
        {
            source = SeedIds.References.DoraReport2024.ToString(), fields = new { licence = "all_rights_reserved", rights_holder = "DORA", accessed_on = "2026-10-19" },
            recheck = new[] { "url" }, accept_redirect = true,
        });

        (await UrlAsync()).ShouldBe(Moved);
        body.GetProperty("status").GetString().ShouldBe("needs_check");

        await _server.Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await UrlAsync()).ShouldBe("https://dora.dev/research/2024/");
    }

    [Test]
    [Order(3)]
    public async Task UpdateSource_WhenNothingHasMoved_ShouldRefuseToAcceptARedirect()
    {
        var error = await _server.Client.ErrorAsync("update_source", new { source = SeedIds.References.BeckXp.ToString(), accept_redirect = true });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("accept_redirect");
    }

    [Test]
    [Order(4)]
    public async Task UpdateSource_WhenFieldsComeWithAnAcceptThatCannotSucceed_ShouldWriteNothing()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        const string counts = "SELECT (SELECT count(*) FROM reference_event) || '/' || (SELECT count(*) FROM reference_check) || '/' || (SELECT count(*) FROM change_set)";
        var before = await connection.ExecuteScalarAsync<string>(counts);
        var publisher = await connection.ExecuteScalarAsync<string>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.BeckXp });

        var error = await _server.Client.ErrorAsync("update_source",
            new { source = SeedIds.References.BeckXp.ToString(), fields = new { publisher = "Agent Press" }, recheck = new[] { "isbn" }, accept_redirect = true });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("accept_redirect");
        (await connection.ExecuteScalarAsync<string>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.BeckXp })).ShouldBe(publisher);
        (await connection.ExecuteScalarAsync<string>(counts)).ShouldBe(before);
    }
}
