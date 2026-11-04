using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// delete_kb_entry and merge_kb_entries over stdio on the Product Engineering seed (#78): two new entries, one mentioned in a
// scene and a summary, linked to a scene and related to a seed entry. In order on one clone: a dry-run writes nothing; delete
// and its undo; merge and its undo put every row back; an undo after an author edit is conflict and changes nothing; a target
// in another book's scope is invalid.
[TestFixture]
[CancelAfter(120000)]
public class McpKbLifecycleTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private Guid _dup;
    private Guid _keep;
    private string _deleteUndo = "";
    private string _mergeUndo = "";
    private string[] _before = [];

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        _dup = Guid.Parse((await Client.OkAsync("upsert_kb_entry", new
        {
            book = Book, name = "Scene 1 voice notes", category = "Research", aliases = new[] { "voice notes" }, body = "Recorded in the car.",
        })).GetProperty("item").GetProperty("id").GetString()!);
        _keep = Guid.Parse((await Client.OkAsync("upsert_kb_entry", new
        {
            book = Book, name = "Plaud transcript", category = "Research", body = "The transcript.",
        })).GetProperty("item").GetProperty("id").GetString()!);
        await using var db = Open();
        await db.ExecuteAsync("UPDATE prose SET body = body || E'\\n\\nHeard on [the notes](kb:' || @dup || ').', version = version + 1 WHERE scene_id = @scene",
            new { dup = _dup, scene = SeedIds.Scenes.Ch5Sc1 });
        await db.ExecuteAsync("UPDATE scene SET summary = 'Recalls [the notes](kb:' || @dup || ').' WHERE id = @scene", new { dup = _dup, scene = SeedIds.Scenes.Ch4Sc1 });
        await db.ExecuteAsync("UPDATE kb_entry SET body = body || ' See [notes](kb:' || @dup || ').' WHERE id = @other", new { dup = _dup, other = SeedIds.KbEntries.GoodhartsLaw });
        await db.ExecuteAsync("INSERT INTO scene_kb VALUES (@scene, @dup), (@scene2, @dup), (@scene2, @keep)",
            new { scene = SeedIds.Scenes.Ch4Sc1, scene2 = SeedIds.Scenes.Ch5Sc2, dup = _dup, keep = _keep });
        await db.ExecuteAsync("INSERT INTO kb_entry_link VALUES (least(@dup, @other), greatest(@dup, @other))", new { dup = _dup, other = SeedIds.KbEntries.GoodhartsLaw });
        _before = await FingerprintAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private NpgsqlConnection Open() => new(_server.ConnectionString);

    private async Task<T?> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = Open();
        return await connection.ExecuteScalarAsync<T>(sql, args);
    }

    // Everything either tool can touch, one line per row (links only while live). Timestamps are left out: a trigger stamps updated_at on any update.
    private async Task<string[]> FingerprintAsync() => (await ScalarAsync<string>(
        """
        SELECT string_agg(x, E'\n' ORDER BY x) FROM (
          SELECT 'prose ' || scene_id || body AS x FROM prose
          UNION ALL SELECT 'summary ' || id || coalesce(summary, '~') FROM scene
          UNION ALL SELECT 'beat ' || id || body FROM beat
          UNION ALL SELECT 'entry ' || id || coalesce(summary, '~') || body || aliases::text || (archived_at IS NULL)::text FROM kb_entry
          UNION ALL SELECT 'scene_kb ' || scene_id || kb_entry_id FROM scene_kb WHERE archived_at IS NULL
          UNION ALL SELECT 'beat_kb ' || beat_id || kb_entry_id FROM beat_kb WHERE archived_at IS NULL
          UNION ALL SELECT 'note ' || id || kb_entry_id || (archived_at IS NULL)::text FROM kb_progression
          UNION ALL SELECT 'link ' || entry_a_id || entry_b_id FROM kb_entry_link WHERE archived_at IS NULL
        ) t
        """))!.Split('\n');

    private async Task<string> ProseOfAsync(Guid scene) => (await ScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", new { scene }))!;

    [Test]
    [Order(1)]
    public async Task DeleteKbEntry_WhenDryRun_ShouldReportTheImpactWithoutWriting()
    {
        var body = await Client.OkAsync("delete_kb_entry", new { entry = _dup.ToString(), dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        body.GetProperty("mentions").GetInt32().ShouldBe(3);
        body.GetProperty("links").GetInt32().ShouldBe(1);
        body.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await FingerprintAsync()).ShouldBe(_before, ignoreOrder: false);
    }

    [Test]
    [Order(2)]
    public async Task DeleteKbEntry_WhenDeleted_ShouldTurnMentionsIntoTextAndDropTheLinks()
    {
        var body = await Client.OkAsync("delete_kb_entry", new { entry = "voice notes", book = Book });

        _deleteUndo = body.GetProperty("undo_id").GetString()!;
        (await ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @dup", new { dup = _dup })).ShouldBeTrue();
        (await ProseOfAsync(SeedIds.Scenes.Ch5Sc1)).ShouldContain("Heard on the notes.");
        (await ScalarAsync<string>("SELECT summary FROM scene WHERE id = @scene", new { scene = SeedIds.Scenes.Ch4Sc1 })).ShouldBe("Recalls the notes.");
        (await ScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE kb_entry_id = @dup AND archived_at IS NULL", new { dup = _dup })).ShouldBe(0);
        (await ScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link WHERE @dup IN (entry_a_id, entry_b_id) AND archived_at IS NULL", new { dup = _dup })).ShouldBe(0);
        // The links are kept, archived.
        (await ScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE kb_entry_id = @dup AND archived_at IS NOT NULL", new { dup = _dup })).ShouldBe(2);
        (await ScalarAsync<int>("SELECT count(*)::int FROM kb_entry_link WHERE @dup IN (entry_a_id, entry_b_id) AND archived_at IS NOT NULL", new { dup = _dup })).ShouldBe(1);
        (await ScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @scene AND source = 'agent'", new { scene = SeedIds.Scenes.Ch5Sc1 })).ShouldBeGreaterThan(0);
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenTheDeleteIsUndone_ShouldBringBackTheEntryItsMentionsAndLinks()
    {
        await Client.OkAsync("undo", new { undo_id = _deleteUndo });

        (await FingerprintAsync()).ShouldBe(_before, ignoreOrder: false);
    }

    [Test]
    [Order(4)]
    public async Task Undo_WhenAnAuthorEditedAFormerMentionAfterTheDelete_ShouldBeConflictAndChangeNothing()
    {
        var undo = (await Client.OkAsync("delete_kb_entry", new { entry = _dup.ToString() })).GetProperty("undo_id").GetString();
        await using (var db = Open())
        {
            await db.ExecuteAsync("UPDATE scene SET summary = 'Rewritten by the author.' WHERE id = @scene", new { scene = SeedIds.Scenes.Ch4Sc1 });
        }
        var after = await FingerprintAsync();

        var error = await Client.ErrorAsync("undo", new { undo_id = undo });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await FingerprintAsync()).ShouldBe(after, ignoreOrder: false);
        // Put the scenario back for the merge: the author's edit is reverted by hand, then the delete is undone.
        await using (var db = Open())
        {
            await db.ExecuteAsync("UPDATE scene SET summary = 'Recalls the notes.' WHERE id = @scene", new { scene = SeedIds.Scenes.Ch4Sc1 });
        }
        await Client.OkAsync("undo", new { undo_id = undo });
        (await FingerprintAsync()).ShouldBe(_before, ignoreOrder: false);
    }

    [Test]
    [Order(5)]
    public async Task MergeKbEntries_WhenDryRun_ShouldReportWhatWouldMoveWithoutWriting()
    {
        var body = await Client.OkAsync("merge_kb_entries", new { entry = _dup.ToString(), into = "Plaud transcript", dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        body.GetProperty("scene_links").GetInt32().ShouldBe(1);
        body.GetProperty("related").GetInt32().ShouldBe(1);
        body.GetProperty("new_aliases").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Scene 1 voice notes", "voice notes"]);
        body.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await FingerprintAsync()).ShouldBe(_before, ignoreOrder: false);
    }

    [Test]
    [Order(6)]
    public async Task MergeKbEntries_WhenMerged_ShouldMoveMentionsLinksAndAliasesAndArchiveTheDuplicate()
    {
        var body = await Client.OkAsync("merge_kb_entries", new { entry = "voice notes", into = "Plaud transcript", book = Book });

        _mergeUndo = body.GetProperty("undo_id").GetString()!;
        (await ProseOfAsync(SeedIds.Scenes.Ch5Sc1)).ShouldContain($"[the notes](kb:{_keep})");
        (await ScalarAsync<string>("SELECT summary FROM scene WHERE id = @scene", new { scene = SeedIds.Scenes.Ch4Sc1 })).ShouldBe($"Recalls [the notes](kb:{_keep}).");
        (await ScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE kb_entry_id = @keep AND archived_at IS NULL", new { keep = _keep })).ShouldBe(2);
        (await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM kb_entry_link WHERE @keep IN (entry_a_id, entry_b_id) AND @other IN (entry_a_id, entry_b_id) AND archived_at IS NULL)",
            new { keep = _keep, other = SeedIds.KbEntries.GoodhartsLaw })).ShouldBeTrue();
        (await ScalarAsync<string>("SELECT array_to_string(aliases, '|') FROM kb_entry WHERE id = @keep", new { keep = _keep })).ShouldBe("Scene 1 voice notes|voice notes");
        (await ScalarAsync<string>("SELECT body FROM kb_entry WHERE id = @keep", new { keep = _keep }))!.ShouldContain("Merged from Scene 1 voice notes");
        (await ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @dup", new { dup = _dup })).ShouldBeTrue();
    }

    [Test]
    [Order(7)]
    public async Task Undo_WhenTheMergeIsUndone_ShouldPutEveryRowBackExactly()
    {
        await Client.OkAsync("undo", new { undo_id = _mergeUndo });

        (await FingerprintAsync()).ShouldBe(_before, ignoreOrder: false);
    }

    [Test]
    [Order(8)]
    public async Task Undo_WhenAnAuthorEditedTheKeptEntryAfterTheMerge_ShouldBeConflictAndChangeNothing()
    {
        var undo = (await Client.OkAsync("merge_kb_entries", new { entry = _dup.ToString(), into = _keep.ToString() })).GetProperty("undo_id").GetString();
        await using (var db = Open())
        {
            await db.ExecuteAsync("UPDATE kb_entry SET body = body || ' Edited.' WHERE id = @keep", new { keep = _keep });
        }
        var after = await FingerprintAsync();

        var error = await Client.ErrorAsync("undo", new { undo_id = undo });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await FingerprintAsync()).ShouldBe(after, ignoreOrder: false);
    }

    [Test]
    [Order(9)]
    public async Task MergeKbEntries_WhenTheTargetIsOutsideTheScope_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("merge_kb_entries", new { entry = SeedIds.KbEntries.GoodhartsLaw.ToString(), into = SeedIds.KbEntries.Gravetouch.ToString() });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(9)]
    public async Task MergeKbEntries_WhenIntoIsTheSameEntry_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("merge_kb_entries", new { entry = "Goodhart's Law", into = SeedIds.KbEntries.GoodhartsLaw.ToString() });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }
}
