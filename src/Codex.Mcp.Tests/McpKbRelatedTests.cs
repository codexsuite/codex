using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// related_add and related_remove on upsert_kb_entry (#78) on the Product Engineering seed: relating on create and on
// update, pairs already in the wanted state changing nothing, an entry outside the scope failing before any write, and
// undo reversing exactly the pairs that changed. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public class McpKbRelatedTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private Guid _probe;
    private string _createUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task UpsertKbEntry_WhenCreatingWithRelatedAdd_ShouldRelateByIdAndByName()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            book = Book, name = "Related Probe", category = "Research", related_add = new[] { SeedIds.KbEntries.GoodhartsLaw.ToString(), "Conway's Law" },
        });

        _probe = Guid.Parse(result.GetProperty("item").GetProperty("id").GetString()!);
        _createUndo = result.GetProperty("undo_id").GetString()!;
        (await RelatedAsync(SeedIds.KbEntries.GoodhartsLaw)).ShouldBeTrue();
        (await RelatedAsync(SeedIds.KbEntries.ConwaysLaw)).ShouldBeTrue();
        result.GetProperty("affected").EnumerateArray().Count(a => a.GetProperty("kind").GetString() == "kb_related").ShouldBe(2);
    }

    [Test]
    [Order(2)]
    public async Task UpsertKbEntry_WhenAddingAnExistingPairAndANewOne_ShouldRecordOnlyTheNewOneAndUndoShouldRemoveOnlyIt()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, related_add = new[] { SeedIds.KbEntries.ConwaysLaw.ToString(), SeedIds.KbEntries.BrooksLaw.ToString() },
        });

        result.GetProperty("affected").EnumerateArray().Count(a => a.GetProperty("kind").GetString() == "kb_related").ShouldBe(1);
        (await RelatedAsync(SeedIds.KbEntries.BrooksLaw)).ShouldBeTrue();
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await RelatedAsync(SeedIds.KbEntries.BrooksLaw)).ShouldBeFalse();
        (await RelatedAsync(SeedIds.KbEntries.ConwaysLaw)).ShouldBeTrue();
    }

    [Test]
    [Order(3)]
    public async Task UpsertKbEntry_WhenRemovingARelatedAndAMissingPair_ShouldRemoveOnlyTheRelatedOneAndUndoShouldPutItBack()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, related_remove = new[] { SeedIds.KbEntries.ConwaysLaw.ToString(), SeedIds.KbEntries.BrooksLaw.ToString() },
        });

        (await RelatedAsync(SeedIds.KbEntries.ConwaysLaw)).ShouldBeFalse();
        result.GetProperty("affected").EnumerateArray().Count(a => a.GetProperty("kind").GetString() == "kb_related").ShouldBe(1);
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await RelatedAsync(SeedIds.KbEntries.ConwaysLaw)).ShouldBeTrue();
        (await RelatedAsync(SeedIds.KbEntries.GoodhartsLaw)).ShouldBeTrue();
    }

    [Test]
    [Order(4)]
    public async Task UpsertKbEntry_WhenNothingChanges_ShouldRecordNoUndo()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, related_add = new[] { SeedIds.KbEntries.GoodhartsLaw.ToString() },
            related_remove = new[] { SeedIds.KbEntries.BrooksLaw.ToString() },
        });

        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
    }

    [Test]
    [Order(5)]
    public async Task UpsertKbEntry_WhenRelatedAddAndSummaryTogether_ShouldUndoBothInOneStep()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, summary = "Probed.", related_add = new[] { SeedIds.KbEntries.BrooksLaw.ToString() },
        });

        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        (await RelatedAsync(SeedIds.KbEntries.BrooksLaw)).ShouldBeFalse();
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QuerySingleAsync<string?>("SELECT summary FROM kb_entry WHERE id = @id", new { id = _probe })).ShouldNotBe("Probed.");
    }

    [Test]
    [Order(6)]
    public async Task UpsertKbEntry_WhenAnEntryIsOutsideTheScope_ShouldFailOutOfScopeBeforeWritingAnything()
    {
        var error = await Client.ErrorAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, summary = "Never written.",
            related_add = new[] { SeedIds.KbEntries.BrooksLaw.ToString(), SeedIds.KbEntries.Gravetouch.ToString() },
        });

        error.GetProperty("code").GetString().ShouldBe("out_of_scope");
        (await RelatedAsync(SeedIds.KbEntries.BrooksLaw)).ShouldBeFalse();
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QuerySingleAsync<string?>("SELECT summary FROM kb_entry WHERE id = @id", new { id = _probe })).ShouldNotBe("Never written.");
    }

    [Test]
    [Order(7)]
    public async Task UpsertKbEntry_WhenCreatingWithAnOutOfScopeRelated_ShouldNotCreateTheEntry()
    {
        var error = await Client.ErrorAsync("upsert_kb_entry", new
        {
            book = Book, name = "Never Created", category = "Research", related_add = new[] { SeedIds.KbEntries.Gravetouch.ToString() },
        });

        error.GetProperty("code").GetString().ShouldBe("out_of_scope");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM kb_entry WHERE name = 'Never Created')")).ShouldBeFalse();
    }

    [Test]
    [Order(8)]
    public async Task UpsertKbEntry_WhenRelatedIsInvalid_ShouldFailInvalidOrNotFound()
    {
        var self = await Client.ErrorAsync("upsert_kb_entry", new { entry = _probe.ToString(), book = Book, related_add = new[] { _probe.ToString() } });
        var both = await Client.ErrorAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, related_add = new[] { SeedIds.KbEntries.BrooksLaw.ToString() },
            related_remove = new[] { SeedIds.KbEntries.BrooksLaw.ToString() },
        });
        var unknown = await Client.ErrorAsync("upsert_kb_entry", new { entry = _probe.ToString(), book = Book, related_add = new[] { "No Such Entry" } });

        self.GetProperty("code").GetString().ShouldBe("invalid");
        both.GetProperty("code").GetString().ShouldBe("invalid");
        unknown.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(9)]
    public async Task Undo_WhenARelatedEntryWasArchivedSince_ShouldBeConflict()
    {
        var removed = await Client.OkAsync("upsert_kb_entry", new
        {
            entry = _probe.ToString(), book = Book, related_remove = new[] { SeedIds.KbEntries.GoodhartsLaw.ToString() },
        });
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        await connection.ExecuteAsync("UPDATE kb_entry SET archived_at = now() WHERE id = @id", new { id = SeedIds.KbEntries.GoodhartsLaw });

        var error = await Client.ErrorAsync("undo", new { undo_id = removed.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        _createUndo.ShouldNotBeEmpty();
    }

    private async Task<bool> RelatedAsync(Guid other)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM kb_entry_link WHERE entry_a_id = least(@a, @b) AND entry_b_id = greatest(@a, @b) AND archived_at IS NULL)", new { a = _probe, b = other });
    }
}
