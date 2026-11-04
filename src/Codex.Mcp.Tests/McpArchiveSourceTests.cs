using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// archive_source over stdio on the Product Engineering seed (#20 review 2). One scenario on its own clone, in order, on The
// Velocity Trap (#4: quotes in Ch 4 · Sc 1 and Ch 8 · Sc 1, and the open unflagged quote in Ch 8 · Sc 1 suggests it): a dry
// run that writes nothing, the archive (markers unwrapped into plain text, source and uses archived, the flag's guess
// cleared), its undo (all of it back), and an undo refused once a scene it changed has been edited since.
[TestFixture]
[CancelAfter(120000)]
public class McpArchiveSourceTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid[] Scenes = [SeedIds.Scenes.Ch4Sc1, SeedIds.Scenes.Ch8Sc1];
    private McpStdioServer _server = null!;
    private Dictionary<Guid, string> _before = [];
    private string _undoId = "";

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

    private async Task<Dictionary<Guid, string>> BodiesAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return (await connection.QueryAsync<(Guid Scene, string Body)>("SELECT scene_id, body FROM prose WHERE scene_id = ANY(@ids)", new { ids = Scenes }))
            .ToDictionary(r => r.Scene, r => r.Body);
    }

    private Task<int> LiveUsesAsync() =>
        ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE reference_id = @id AND archived_at IS NULL", new { id = SeedIds.References.VelocityTrap });

    private Task<bool> ArchivedAsync() =>
        ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap });

    private Task<int> FlagsNamingItAsync() =>
        ScalarAsync<int>("SELECT count(*)::int FROM quote_flag WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap });

    [Test]
    [Order(1)]
    public async Task ArchiveSource_WhenDryRun_ShouldReportTheUsesAndScenesWithoutWriting()
    {
        _before = await BodiesAsync();

        var body = await Client.OkAsync("archive_source", new { source = "#4", book = Book, dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        body.GetProperty("uses").GetInt32().ShouldBe(2);
        body.GetProperty("scenes").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ShouldBe(Scenes, ignoreOrder: true);
        body.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await ArchivedAsync()).ShouldBeFalse();
        (await LiveUsesAsync()).ShouldBe(2);
        (await BodiesAsync()).ShouldBe(_before);
    }

    [Test]
    [Order(2)]
    public async Task ArchiveSource_WhenArchived_ShouldUnwrapItsMarkersAndArchiveTheSourceAndItsUses()
    {
        (await FlagsNamingItAsync()).ShouldBeGreaterThan(0);

        var body = await Client.OkAsync("archive_source", new { source = "The Velocity Trap", book = Book });

        _undoId = body.GetProperty("undo_id").GetString()!;
        body.GetProperty("uses").GetInt32().ShouldBe(2);
        body.GetProperty("side_effects").EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.StartsWith("Removed #4 · The Velocity Trap"));
        (await ArchivedAsync()).ShouldBeTrue();
        (await LiveUsesAsync()).ShouldBe(0);
        (await FlagsNamingItAsync()).ShouldBe(0);
        var after = await BodiesAsync();
        foreach (var scene in Scenes)
        {
            after[scene].ShouldNotBe(_before[scene]);
            after[scene].ShouldNotContain($"(ref:{SeedIds.ReferenceUses.VelocityTrapCh4Sc1})");
            after[scene].ShouldNotContain($"(ref:{SeedIds.ReferenceUses.VelocityTrapCh8Sc1})");
        }
        (await ScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = ANY(@ids) AND source = 'agent'", new { ids = Scenes })).ShouldBe(2);
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenAnArchiveIsUndone_ShouldBringBackTheSourceItsUsesTheProseAndTheFlag()
    {
        await Client.OkAsync("undo", new { undo_id = _undoId });

        (await ArchivedAsync()).ShouldBeFalse();
        (await LiveUsesAsync()).ShouldBe(2);
        (await FlagsNamingItAsync()).ShouldBeGreaterThan(0);
        (await BodiesAsync()).ShouldBe(_before);
        var clearance = await Client.OkAsync("get_clearance", new { book = Book, source = "#4" });
        clearance.GetProperty("item").GetProperty("label").GetString().ShouldBe("#4 · The Velocity Trap");
    }

    [Test]
    [Order(4)]
    public async Task Undo_WhenAChangedSceneWasEditedSince_ShouldFailConflictAndLeaveTheSourceArchived()
    {
        var body = await Client.OkAsync("archive_source", new { source = "#4", book = Book });
        await using (var connection = new NpgsqlConnection(_server.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE prose SET body = body || E'\\n\\nA later line.', version = version + 1 WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch8Sc1 });
        }

        var error = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ArchivedAsync()).ShouldBeTrue();
        (await LiveUsesAsync()).ShouldBe(0);
    }
}
