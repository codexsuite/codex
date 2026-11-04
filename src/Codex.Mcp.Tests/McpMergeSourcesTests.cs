using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// merge_sources over stdio on the Product Engineering seed (M10). One scenario on its own clone, in order: dry-run folding The
// Velocity Trap (#4, 342 quoted words, a copyright risk) into Accelerate (#8), the real merge (uses move, empty fields fill,
// the duplicate is archived, the prose is not touched, the kept source's clearance is recomputed), its undo, a permission
// request following its source, an undo refused after the author changed a moved use, and the errors.
[TestFixture]
[CancelAfter(120000)]
public class McpMergeSourcesTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private string _mergeUndoId = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        // The duplicate knows an ISBN and a DOI the kept source lacks.
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        await connection.ExecuteAsync("UPDATE reference SET isbn = '9780000000002', doi = '10.1000/velocity' WHERE id = @id", new { id = SeedIds.References.VelocityTrap });
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<T?> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args);
    }

    private Task<int> UsesOfAsync(Guid source) =>
        ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE reference_id = @source", new { source });

    private Task<string?> ProseFingerprintAsync() => ScalarAsync<string>("SELECT md5(string_agg(body, '|' ORDER BY scene_id)) FROM prose");

    [Test]
    [Order(1)]
    public async Task MergeSources_WhenDryRun_ShouldReportWhatWouldMoveAndTheNewClearanceWithoutWriting()
    {
        var velocityUses = await UsesOfAsync(SeedIds.References.VelocityTrap);
        var accelerateUses = await UsesOfAsync(SeedIds.References.Accelerate);

        var body = await Client.OkAsync("merge_sources", new { source = "#4", into = "#8", book = Book, dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        body.GetProperty("uses_moved").GetInt32().ShouldBe(velocityUses);
        body.GetProperty("status_before").GetString().ShouldNotBe("risk");
        body.GetProperty("status").GetString().ShouldBe("risk");
        body.GetProperty("filled_fields").EnumerateArray().Select(f => f.GetString()).ShouldBe(["isbn", "doi"], ignoreOrder: true);
        body.TryGetProperty("undo_id", out var undoId).ShouldBeFalse(undoId.ToString());
        (await UsesOfAsync(SeedIds.References.VelocityTrap)).ShouldBe(velocityUses);
        (await UsesOfAsync(SeedIds.References.Accelerate)).ShouldBe(accelerateUses);
        (await ScalarAsync<bool>("SELECT archived_at IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBeTrue();
        (await ScalarAsync<string?>("SELECT isbn FROM reference WHERE id = @id", new { id = SeedIds.References.Accelerate })).ShouldBeNull();
    }

    [Test]
    [Order(2)]
    public async Task MergeSources_WhenMerged_ShouldMoveTheUsesFillEmptyFieldsArchiveTheDuplicateAndLeaveTheProse()
    {
        var velocityUses = await UsesOfAsync(SeedIds.References.VelocityTrap);
        var accelerateUses = await UsesOfAsync(SeedIds.References.Accelerate);
        var prose = await ProseFingerprintAsync();

        var body = await Client.OkAsync("merge_sources", new { source = "#4", into = "#8", book = Book });

        body.GetProperty("status").GetString().ShouldBe("risk");
        body.GetProperty("item").GetProperty("label").GetString()!.ShouldContain("Accelerate");
        _mergeUndoId = body.GetProperty("undo_id").GetString()!;
        _mergeUndoId.ShouldNotBeNullOrEmpty();
        (await UsesOfAsync(SeedIds.References.Accelerate)).ShouldBe(accelerateUses + velocityUses);
        (await UsesOfAsync(SeedIds.References.VelocityTrap)).ShouldBe(0);
        (await ScalarAsync<string>("SELECT isbn FROM reference WHERE id = @id", new { id = SeedIds.References.Accelerate })).ShouldBe("9780000000002");
        (await ScalarAsync<string>("SELECT publisher FROM reference WHERE id = @id", new { id = SeedIds.References.Accelerate })).ShouldBe("IT Revolution");
        (await ScalarAsync<Guid?>("SELECT merged_into_id FROM reference WHERE id = @id AND archived_at IS NOT NULL", new { id = SeedIds.References.VelocityTrap }))
            .ShouldBe(SeedIds.References.Accelerate);
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'merged_in'", new { id = SeedIds.References.Accelerate })).ShouldBe("agent");
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'merged_into'", new { id = SeedIds.References.VelocityTrap })).ShouldBe("agent");
        (await ProseFingerprintAsync()).ShouldBe(prose);
        var clearance = await Client.OkAsync("get_clearance", new { source = "#8", book = Book });
        clearance.GetProperty("focus").GetProperty("next_action").GetString().ShouldNotBeNullOrEmpty();
        (await Client.ErrorAsync("get_clearance", new { source = SeedIds.References.VelocityTrap.ToString() })).GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenAMergeIsUndone_ShouldRestoreTheDuplicateItsUsesAndTheKeptSourcesFields()
    {
        var velocityUses = await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE id = ANY(@ids)",
            new { ids = new[] { SeedIds.ReferenceUses.VelocityTrapCh4Sc1, SeedIds.ReferenceUses.VelocityTrapCh8Sc1 } });

        await Client.OkAsync("undo", new { undo_id = _mergeUndoId });

        (await UsesOfAsync(SeedIds.References.VelocityTrap)).ShouldBeGreaterThanOrEqualTo(velocityUses);
        (await ScalarAsync<bool>("SELECT archived_at IS NULL AND merged_into_id IS NULL FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBeTrue();
        (await ScalarAsync<string?>("SELECT isbn FROM reference WHERE id = @id", new { id = SeedIds.References.Accelerate })).ShouldBeNull();
        (await ScalarAsync<string?>("SELECT doi FROM reference WHERE id = @id", new { id = SeedIds.References.Accelerate })).ShouldBeNull();
        (await ScalarAsync<string>("SELECT isbn FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBe("9780000000002");
        (await Client.OkAsync("get_clearance", new { source = "#4", book = Book })).GetProperty("focus").GetProperty("next_action").GetString().ShouldNotBeNullOrEmpty();
        (await Client.OkAsync("merge_sources", new { source = "#4", into = "#8", book = Book, dry_run = true })).GetProperty("status").GetString().ShouldBe("risk");
    }

    [Test]
    [Order(4)]
    public async Task MergeSources_WhenTheDuplicateHasAPermissionRequestAndTheKeptSourceHasNone_ShouldMoveItAndUndoShouldMoveItBack()
    {
        var merged = await Client.OkAsync("merge_sources", new { source = "#9", into = "#11", book = Book });

        merged.GetProperty("permission_moved").GetBoolean().ShouldBeTrue();
        (await ScalarAsync<Guid>("SELECT reference_id FROM permission_request WHERE id = @id", new { id = SeedIds.PermissionRequests.TeamTopologies }))
            .ShouldBe(SeedIds.References.ShipItFriday);

        await Client.OkAsync("undo", new { undo_id = merged.GetProperty("undo_id").GetString() });

        (await ScalarAsync<Guid>("SELECT reference_id FROM permission_request WHERE id = @id", new { id = SeedIds.PermissionRequests.TeamTopologies }))
            .ShouldBe(SeedIds.References.TeamTopologies);
    }

    [Test]
    [Order(5)]
    public async Task MergeSources_WhenTheKeptSourceAlreadyHasARequest_ShouldLeaveTheDuplicatesRequestWhereItIs()
    {
        await Client.OkAsync("draft_permission_letter", new { source = "#11", book = Book });

        var merged = await Client.OkAsync("merge_sources", new { source = "#9", into = "#11", book = Book });

        merged.GetProperty("permission_moved").GetBoolean().ShouldBeFalse();
        (await ScalarAsync<Guid>("SELECT reference_id FROM permission_request WHERE id = @id", new { id = SeedIds.PermissionRequests.TeamTopologies }))
            .ShouldBe(SeedIds.References.TeamTopologies);
        await Client.OkAsync("undo", new { undo_id = merged.GetProperty("undo_id").GetString() });
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenAMovedUseWasRetypedSinceTheMerge_ShouldBeAConflictAndLeaveTheMerge()
    {
        var merged = await Client.OkAsync("merge_sources", new { source = "#4", into = "#8", book = Book });
        await ScalarAsync<int>("UPDATE reference_use SET kind = 'paraphrase' WHERE id = @id RETURNING 1", new { id = SeedIds.ReferenceUses.VelocityTrapCh4Sc1 });

        var error = await Client.ErrorAsync("undo", new { undo_id = merged.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ScalarAsync<Guid>("SELECT reference_id FROM reference_use WHERE id = @id", new { id = SeedIds.ReferenceUses.VelocityTrapCh4Sc1 })).ShouldBe(SeedIds.References.Accelerate);
        (await ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference WHERE id = @id", new { id = SeedIds.References.VelocityTrap })).ShouldBeTrue();
    }

    [Test]
    [Order(7)]
    public async Task MergeSources_WhenTheSourcesCannotBeMerged_ShouldBeInvalidOrNotFound()
    {
        var foreign = await ScalarAsync<Guid>(
            "INSERT INTO reference (book_id, kind, title) VALUES (@book, 'book', 'A source in another book') RETURNING id", new { book = SeedIds.Books.HollowThrone });

        (await Client.ErrorAsync("merge_sources", new { source = "#1", into = "#1", book = Book })).GetProperty("field").GetString().ShouldBe("into");
        (await Client.ErrorAsync("merge_sources", new { source = foreign.ToString(), into = SeedIds.References.BeckXp.ToString() })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("merge_sources", new { source = "#1", into = "#99", book = Book })).GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("merge_sources", new { source = "#99", into = "#1", book = Book })).GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("merge_sources", new { source = "", into = "#1", book = Book })).GetProperty("code").GetString().ShouldBe("invalid");
    }
}
