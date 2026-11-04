using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// draft_permission_letter over stdio on the Product Engineering seed (split out of manage_permission, M10). One scenario on its
// own clone, in order: draft a request for The Velocity Trap from its uses, draft one with the letter and address overridden,
// undo a draft, undo refused once the author edited the draft, and the state errors.
[TestFixture]
[CancelAfter(120000)]
public class McpDraftPermissionLetterTests
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

    [Test]
    [Order(1)]
    public async Task DraftPermissionLetter_WhenARequestIsDrafted_ShouldWriteTheLetterFromTheUsesAndStayARisk()
    {
        var body = await Client.OkAsync("draft_permission_letter", new { source = "#4", book = Book });

        body.GetProperty("status").GetString().ShouldBe("draft");
        body.GetProperty("letter").GetString()!.ShouldContain("The Velocity Trap");
        body.GetProperty("source_status").GetString().ShouldBe("risk");
        body.GetProperty("source_reason").GetString().ShouldBe("Permission drafted, not sent");
        body.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        (await ScalarAsync<string>("SELECT status FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBe("draft");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'permission_drafted'", new { id = SeedIds.References.VelocityTrap })).ShouldBe("agent");
    }

    [Test]
    [Order(2)]
    public async Task DraftPermissionLetter_WhenTheLetterAndAddressAreGiven_ShouldUseThemInsteadOfTheDraftedLetter()
    {
        var body = await Client.OkAsync("draft_permission_letter",
            new { source = "#3", book = Book, letter = "Dear Reserve Bank,\n\nMay I quote p. 4?", to_address = "rights@rba.example" });

        body.GetProperty("letter").GetString().ShouldBe("Dear Reserve Bank,\n\nMay I quote p. 4?");
        body.GetProperty("to").GetString().ShouldBe("rights@rba.example");
        (await ScalarAsync<string>("SELECT to_address FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.GoodhartMonetary })).ShouldBe("rights@rba.example");
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenADraftIsUndone_ShouldWithdrawTheRequest()
    {
        var drafted = await Client.OkAsync("draft_permission_letter", new { source = "#11", book = Book });

        await Client.OkAsync("undo", new { undo_id = drafted.GetProperty("undo_id").GetString() });

        (await ScalarAsync<string>("SELECT status FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.ShipItFriday })).ShouldBe("withdrawn");
        (await Client.OkAsync("get_clearance", new { source = "#11" })).GetProperty("focus").GetProperty("next_action").GetString().ShouldBe("cut_or_request");
    }

    [Test]
    [Order(4)]
    public async Task Undo_WhenTheDraftWasEditedSince_ShouldBeAConflictAndLeaveTheRequest()
    {
        var drafted = await Client.OkAsync("draft_permission_letter", new { source = "#11", book = Book });
        await Client.OkAsync("manage_permission", new { source = "#11", book = Book, action = "update", letter = "Dear Standup Records,\n\nMay I quote the chorus?" });

        var error = await Client.ErrorAsync("undo", new { undo_id = drafted.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ScalarAsync<string>("SELECT status FROM permission_request WHERE reference_id = @id AND status <> 'withdrawn'", new { id = SeedIds.References.ShipItFriday })).ShouldBe("draft");
    }

    [Test]
    [Order(5)]
    public async Task DraftPermissionLetter_WhenTheStateOrArgumentsAreWrong_ShouldBeAConflictInvalidOrNotFound()
    {
        (await Client.ErrorAsync("draft_permission_letter", new { source = "#9", book = Book })).GetProperty("code").GetString().ShouldBe("conflict");
        (await Client.ErrorAsync("draft_permission_letter", new { source = "#99", book = Book })).GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("draft_permission_letter", new { source = "#7", book = Book, letter = "  " })).GetProperty("field").GetString().ShouldBe("letter");
    }
}
