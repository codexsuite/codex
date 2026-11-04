using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests;

// Undo of the References tools when the author edited the same thing since (#20 Safety, M14): update_source's fields, a retyped
// use and a permission draft each go back only while they still read what the agent left; the author's own edit, made through
// the References command, makes undo a conflict, writes nothing and keeps the author's value. One scenario on its own clone.
[TestFixture]
[CancelAfter(120000)]
public class McpUndoAfterAuthorEditTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<string?> PublisherAsync() =>
        await _server.ScalarAsync<string>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.BeckXp });

    [Test]
    [Order(1)]
    public async Task Undo_WhenTheAuthorEditedTheSameFieldAfterUpdateSource_ShouldBeAConflictAndKeepTheAuthorsValue()
    {
        var edit = await Client.OkAsync("update_source", new { source = "#1", book = Book, fields = new { publisher = "Agent Press" } });
        (await PublisherAsync()).ShouldBe("Agent Press");
        var details = (await _server.ApiAsync($"/api/references/{SeedIds.References.BeckXp}")).GetProperty("details");
        await _server.PatchApiAsync($"/api/references/{SeedIds.References.BeckXp}", new
        {
            details = new
            {
                kind = details.GetProperty("kind").GetString(), title = details.GetProperty("title").GetString(), authorsText = details.GetProperty("authorsText").GetString(),
                year = details.GetProperty("year").GetInt32(), publisher = "Author House", edition = details.GetProperty("edition").GetString(),
                isbn = details.GetProperty("isbn").GetString(), doi = details.GetProperty("doi").GetString(), url = details.GetProperty("url").GetString(),
                accessedOn = details.GetProperty("accessedOn").GetString(), licence = details.GetProperty("licence").GetString(),
                rightsHolder = details.GetProperty("rightsHolder").GetString(),
            },
        });

        var error = await Client.ErrorAsync("undo", new { undo_id = edit.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await PublisherAsync()).ShouldBe("Author House");
    }

    [Test]
    [Order(2)]
    public async Task Undo_WhenTheAuthorRetypedTheUseAfterUpdateSourceUse_ShouldBeAConflictAndKeepTheAuthorsKind()
    {
        var use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
        var edit = await Client.OkAsync("update_source_use", new { use, kind = "paraphrase" });
        await _server.PatchApiAsync($"/api/reference-uses/{use}", new { kind = "data" });

        var error = await Client.ErrorAsync("undo", new { undo_id = edit.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await _server.ScalarAsync<string>("SELECT kind FROM reference_use WHERE id = @id", new { id = use })).ShouldBe("data");
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenTheAuthorMarkedTheDraftSent_ShouldBeAConflictAndKeepTheSentRequest()
    {
        var draft = await Client.OkAsync("draft_permission_letter", new { source = "#4", book = Book });
        var request = await _server.ScalarAsync<Guid>("SELECT id FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap });
        await _server.PostApiAsync($"/api/permission-requests/{request}/sent");

        var error = await Client.ErrorAsync("undo", new { undo_id = draft.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await _server.ScalarAsync<string>("SELECT status FROM permission_request WHERE id = @id", new { id = request })).ShouldBe("sent");
    }

    [Test]
    [Order(4)]
    public async Task Undo_WhenNothingChangedSinceTheDraft_ShouldWithdrawIt()
    {
        var draft = await Client.OkAsync("draft_permission_letter", new { source = "#11", book = Book });
        var request = await _server.ScalarAsync<Guid>("SELECT id FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.ShipItFriday });

        await Client.OkAsync("undo", new { undo_id = draft.GetProperty("undo_id").GetString() });

        (await _server.ScalarAsync<string>("SELECT status FROM permission_request WHERE id = @id", new { id = request })).ShouldBe("withdrawn");
    }

    [Test]
    [Order(5)]
    public async Task DraftPermissionLetter_WhenTheLetterIsBlank_ShouldBeInvalidAndDraftNothing()
    {
        var events = await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE reference_id = @id", new { id = SeedIds.References.AgileManifestoWeb });
        var requests = await _server.ScalarAsync<int>("SELECT count(*)::int FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.AgileManifestoWeb });

        var error = await Client.ErrorAsync("draft_permission_letter", new { source = "#2", book = Book, letter = "   " });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("letter");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM permission_request WHERE reference_id = @id", new { id = SeedIds.References.AgileManifestoWeb })).ShouldBe(requests);
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_event WHERE reference_id = @id", new { id = SeedIds.References.AgileManifestoWeb })).ShouldBe(events);
    }
}
