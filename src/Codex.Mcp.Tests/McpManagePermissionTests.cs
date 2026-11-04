using System.Globalization;
using Codex.Contracts;
using Codex.Core;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// manage_permission over stdio on the Product Engineering seed (Team Topologies has a sent request, 16 days old). One
// scenario on its own clone, in order: draft a request for The Velocity Trap (draft_permission_letter, which has its own
// tests in McpDraftPermissionLetterTests), edit it, mark it sent (still a risk, "Permission asked, no reply"), set a
// reminder, refuse recording a grant over MCP, record a refusal; plus the state errors and the actions that can't be undone.
[TestFixture]
[CancelAfter(120000)]
public class McpManagePermissionTests
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
    public async Task ManagePermission_WhenARequestIsDraftedFirst_ShouldGiveItADraftToWorkOn()
    {
        var body = await Client.OkAsync("draft_permission_letter", new { source = "#4", book = Book });

        body.GetProperty("status").GetString().ShouldBe("draft");
        body.GetProperty("letter").GetString()!.ShouldContain("The Velocity Trap");
        body.GetProperty("source_status").GetString().ShouldBe("risk");
        body.GetProperty("source_reason").GetString().ShouldBe("Permission drafted, not sent");
        (await ScalarAsync<string>("SELECT status FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBe("draft");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'permission_drafted'", new { id = SeedIds.References.VelocityTrap })).ShouldBe("agent");
    }

    [Test]
    [Order(2)]
    public async Task ManagePermission_WhenADraftIsEditedAndSent_ShouldRecordItAsSentAndStayARisk()
    {
        var updated = await Client.OkAsync("manage_permission",
            new { source = "#4", book = Book, action = "update", letter = "Dear Whitlock Press,\n\nMay I quote pp. 41-43 and 118?", to_address = "rights@whitlock.example" });
        updated.GetProperty("to").GetString().ShouldBe("rights@whitlock.example");

        var sent = await Client.OkAsync("manage_permission", new { source = "#4", book = Book, action = "mark_sent" });

        sent.GetProperty("status").GetString().ShouldBe("sent");
        sent.GetProperty("source_status").GetString().ShouldBe("risk");
        sent.GetProperty("source_reason").GetString().ShouldBe("Permission asked, no reply");
        (await Client.OkAsync("get_clearance", new { source = "#4" })).GetProperty("focus").GetProperty("next_action").GetString().ShouldBe("chase_permission");
    }

    [Test]
    [Order(3)]
    public async Task ManagePermission_WhenAReminderIsSet_ShouldShowItAndAnInvalidDateShouldBeRefused()
    {
        var remindOn = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var body = await Client.OkAsync("manage_permission", new { source = "#4", book = Book, action = "set_reminder", remind_at = remindOn });

        body.GetProperty("reminder").GetString().ShouldNotBeNullOrWhiteSpace();
        (await Client.ErrorAsync("manage_permission", new { source = "#4", book = Book, action = "set_reminder", remind_at = "next week" }))
            .GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(4)]
    public async Task ManagePermission_WhenAGrantIsRecorded_ShouldBeBlockedBecauseTheSignedLetterIsAFile()
    {
        var error = await Client.ErrorAsync("manage_permission", new { source = "#4", book = Book, action = "record_grant", credit_line = "Reprinted with permission." });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("signed_letter_is_a_file");
        error.GetProperty("message").GetString()!.ShouldContain($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.VelocityTrap}");
        (await ScalarAsync<string>("SELECT status FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBe("sent");
    }

    [Test]
    [Order(5)]
    public async Task ManagePermission_WhenARefusalIsRecorded_ShouldKeepTheRiskAndAllowANewDraft()
    {
        var refused = await Client.OkAsync("manage_permission", new { source = "#4", book = Book, action = "record_refusal", note = "Not for reuse." });

        refused.GetProperty("status").GetString().ShouldBe("refused");
        refused.GetProperty("source_reason").GetString().ShouldBe("Permission refused");
        var undo = await Client.ErrorAsync("undo", new { undo_id = refused.GetProperty("undo_id").GetString() });
        undo.GetProperty("code").GetString().ShouldBe("blocked");
        (await Client.OkAsync("draft_permission_letter", new { source = "#4", book = Book })).GetProperty("status").GetString().ShouldBe("draft");
    }

    [Test]
    [Order(6)]
    public async Task ManagePermission_WhenTheStateDoesNotAllowTheAction_ShouldBeAConflictOrInvalid()
    {
        (await Client.ErrorAsync("manage_permission", new { source = "#9", book = Book, action = "draft" })).GetProperty("field").GetString().ShouldBe("action");
        (await Client.ErrorAsync("manage_permission", new { source = "#1", book = Book, action = "mark_sent" })).GetProperty("code").GetString().ShouldBe("conflict");
        (await Client.ErrorAsync("manage_permission", new { source = "#9", book = Book, action = "send_mail" })).GetProperty("field").GetString().ShouldBe("action");
        (await Client.ErrorAsync("manage_permission", new { source = "#99", book = Book, action = "mark_sent" })).GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(7)]
    public async Task ManagePermission_WhenTheSentRequestIsWithdrawn_ShouldLeaveTheSourceWithNoRequest()
    {
        var body = await Client.OkAsync("manage_permission", new { source = "#9", book = Book, action = "withdraw" });

        body.GetProperty("status").GetString().ShouldBe("withdrawn");
        (await ScalarAsync<string>("SELECT status FROM permission_request WHERE id = @id", new { id = SeedIds.PermissionRequests.TeamTopologies })).ShouldBe("withdrawn");
        body.GetProperty("source_reason").GetString().ShouldNotBe("Permission asked, no reply");
    }
}
