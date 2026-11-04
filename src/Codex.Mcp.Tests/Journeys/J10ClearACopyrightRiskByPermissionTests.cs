using System.Globalization;
using Codex.Contracts;
using Codex.Core;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J10 · Clear a copyright risk, path 2 (ask permission), as the agent does it (#16 J10, #20 draft_permission_letter, manage_permission): Draft request
// writes the letter from the uses (342 words, Ch 4 and 8), the author sends it from their own mail, Mark sent records that:
// the status stays Copyright risk, "Permission asked, no reply". Recording the grant needs the signed letter as a file, so
// over MCP it is blocked with the References link (#16: "Granted, attach the letter"). The HTTP API must agree throughout.
[TestFixture]
[Category("J10")]
[CancelAfter(120000)]
public class J10ClearACopyrightRiskByPermissionTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task DraftPermissionLetter_WhenARequestIsDrafted_ShouldWriteTheLetterFromTheUses()
    {
        var body = await Client.OkAsync("draft_permission_letter", new { source = "#4", book = Book });

        body.GetProperty("status").GetString().ShouldBe("draft");
        var letter = body.GetProperty("letter").GetString()!;
        letter.ShouldContain("The Velocity Trap");
        letter.ShouldContain("342");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("permission").GetProperty("status").GetString().ShouldBe("draft");
        http.GetProperty("permission").GetProperty("letter").GetString().ShouldBe(letter);
    }

    [Test]
    [Order(2)]
    public async Task ManagePermission_WhenTheAuthorHasSentItAndTheAgentMarksItSent_ShouldStayARiskWithNoReplyYet()
    {
        var body = await Client.OkAsync("manage_permission", new { source = "#4", book = Book, action = "mark_sent" });

        body.GetProperty("status").GetString().ShouldBe("sent");
        body.GetProperty("source_status").GetString().ShouldBe("risk");
        body.GetProperty("source_reason").GetString().ShouldBe("Permission asked, no reply");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("verdict").GetProperty("status").GetString().ShouldBe("risk");
        http.GetProperty("permission").GetProperty("status").GetString().ShouldBe("sent");
        (await Client.OkAsync("get_clearance", new { book = Book, source = "#4" })).GetProperty("focus").GetProperty("next_action").GetString().ShouldBe("chase_permission");
    }

    [Test]
    [Order(3)]
    public async Task ManagePermission_WhenTheReplyComesAndAGrantIsRecorded_ShouldBeBlockedWithTheReferencesLink()
    {
        var error = await Client.ErrorAsync("manage_permission", new { source = "#4", book = Book, action = "record_grant", credit_line = "Reprinted with permission." });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("message").GetString()!.ShouldContain($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.VelocityTrap}");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        http.GetProperty("permission").GetProperty("status").GetString().ShouldBe("sent");
    }

    [Test]
    [Order(4)]
    public async Task ManagePermission_WhenTheSeededTeamTopologiesRequestIsChased_ShouldTakeANewReminder()
    {
        var before = await Client.OkAsync("get_clearance", new { book = Book, source = "#9", include = new[] { "permission" } });
        before.GetProperty("focus").GetProperty("next_action").GetString().ShouldBe("chase_permission");

        var remindOn = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone).AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var body = await Client.OkAsync("manage_permission", new { source = "#9", book = Book, action = "set_reminder", remind_at = remindOn });

        body.GetProperty("status").GetString().ShouldBe("sent");
        var http = await _server.ApiAsync($"/api/references/{SeedIds.References.TeamTopologies}");
        http.GetProperty("permission").GetProperty("remindOn").GetString().ShouldBe(remindOn);
    }
}
