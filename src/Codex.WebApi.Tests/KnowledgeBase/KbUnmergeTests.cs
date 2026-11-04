using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.Errors;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.KnowledgeBase;

// KbMergeCommands.UnmergeAsync at the application level (no HTTP route): a merge of an entry that is mentioned in a scene, a
// summary, a beat, another entry, linked, noted, related and cited, then its undo, in order on one cloned schema. The undo puts
// every row back exactly; after an author edit it is a 409 and writes nothing.
[TestFixture]
public class KbUnmergeTests : BookContentFixture
{
    private Guid _dup;
    private Guid _keep;
    private Guid _other;
    private Guid _scene;
    private Guid _scene2;
    private KbMergeSnapshot _snapshot = null!;
    private string _before = "";

    private IKbMergeCommands Merges => Host.Services.GetRequiredService<IKbMergeCommands>();

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var book = await AddBookAsync("Tides");
        var people = await AddCategoryAsync(book, "People");
        _dup = await AddEntryAsync(book, people, "Annie", "a0", ["A."]);
        _keep = await AddEntryAsync(book, people, "Anna", "a1", ["Ann"]);
        _other = await AddEntryAsync(book, people, "Ben", "a2");
        var chapter = await AddChapterAsync(book);
        _scene = await AddSceneAsync(book, chapter, "a0");
        _scene2 = await AddSceneAsync(book, chapter, "a1", "Nothing here.");
        var beat = await AddBeatAsync(_scene);
        var source = await AddSourceAsync(book, "A memoir");
        await using var db = await OpenDatabaseAsync();
        var note = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO kb_progression (kb_entry_id, scene_id, body) VALUES (@dup, @scene, 'Annie flies.') RETURNING id", new { dup = _dup, scene = _scene });
        await db.ExecuteAsync("UPDATE prose SET body = @body WHERE scene_id = @scene",
            new { scene = _scene, body = $"::beat{{id=\"{beat}\"}}\n\nHi [Annie](kb:{_dup}).\n\n::kb-addition{{id=\"{note}\"}}\n\nBye [Annie](kb:{_dup})." });
        await db.ExecuteAsync("UPDATE scene SET summary = @summary WHERE id = @scene", new { scene = _scene2, summary = $"About [Annie](kb:{_dup})." });
        await db.ExecuteAsync("UPDATE beat SET body = @body WHERE id = @beat", new { beat, body = $"[Annie](kb:{_dup}) enters" });
        await db.ExecuteAsync("UPDATE kb_entry SET body = @body, summary = @summary WHERE id = @other",
            new { other = _other, body = $"Friend of [Annie](kb:{_dup}).", summary = "A friend." });
        await db.ExecuteAsync("UPDATE kb_entry SET body = 'Keeps the log.' WHERE id = @keep", new { keep = _keep });
        await db.ExecuteAsync("INSERT INTO scene_kb VALUES (@scene, @dup), (@scene, @keep), (@scene2, @dup)", new { scene = _scene, scene2 = _scene2, dup = _dup, keep = _keep });
        await db.ExecuteAsync("INSERT INTO beat_kb VALUES (@beat, @dup)", new { beat, dup = _dup });
        await db.ExecuteAsync("INSERT INTO kb_entry_link VALUES (least(@dup, @other), greatest(@dup, @other)), (least(@dup, @keep), greatest(@dup, @keep))",
            new { dup = _dup, other = _other, keep = _keep });
        await db.ExecuteAsync("UPDATE reference SET kb_entry_id = @dup WHERE id = @source", new { dup = _dup, source });
        _before = await FingerprintAsync();
    }

    [Test, Order(1)]
    public async Task Unmerge_WhenMergedAndNothingChanged_ShouldPutEveryRowBackExactly()
    {
        var merged = await Merges.MergeWithSnapshotAsync(_dup, _keep, CancellationToken.None);
        _snapshot = merged.Snapshot;
        (await FingerprintAsync()).ShouldNotBe(_before);
        (await Merges.UnmergeProblemAsync(_snapshot, CancellationToken.None)).ShouldBeNull();

        var restored = await Merges.UnmergeAsync(_snapshot, CancellationToken.None);

        restored.Id.ShouldBe(_dup);
        (await FingerprintAsync()).Split('\n').ShouldBe(_before.Split('\n'), ignoreOrder: false);
        // The links the merge gave the target are archived again, not deleted: scene 2, the beat and Ben.
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            """
            SELECT (SELECT count(*) FROM scene_kb WHERE kb_entry_id = @keep AND archived_at IS NOT NULL)
                 + (SELECT count(*) FROM beat_kb WHERE kb_entry_id = @keep AND archived_at IS NOT NULL)
                 + (SELECT count(*) FROM kb_entry_link WHERE @keep IN (entry_a_id, entry_b_id) AND archived_at IS NOT NULL)
            """, new { keep = _keep })).ShouldBe(3);
    }

    [Test, Order(2)]
    public async Task Unmerge_WhenTheSnapshotIsUsedAgain_ShouldBeNotFoundBecauseTheEntryIsLive()
    {
        var error = await Should.ThrowAsync<ApiException>(() => Merges.UnmergeAsync(_snapshot, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(3)]
    public async Task Unmerge_WhenAnAuthorEditedATouchedRowSince_ShouldConflictAndWriteNothing()
    {
        _snapshot = (await Merges.MergeWithSnapshotAsync(_dup, _keep, CancellationToken.None)).Snapshot;
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("UPDATE kb_entry SET body = body || ' More.' WHERE id = @keep", new { keep = _keep });
        }
        var after = await FingerprintAsync();

        (await Merges.UnmergeProblemAsync(_snapshot, CancellationToken.None)).ShouldNotBeNull();
        var error = await Should.ThrowAsync<ApiException>(() => Merges.UnmergeAsync(_snapshot, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.Conflict);
        (await FingerprintAsync()).ShouldBe(after);
    }

    // Everything a merge can touch, one line per row, as text; links only while live. Prose versions are left out: an undo is a new version.
    private async Task<string> FingerprintAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return (await db.ExecuteScalarAsync<string>(
            """
            SELECT string_agg(x, E'\n' ORDER BY x) FROM (
              SELECT 'prose ' || scene_id || body AS x FROM prose
              UNION ALL SELECT 'summary ' || id || coalesce(summary, '~') FROM scene
              UNION ALL SELECT 'beat ' || id || body FROM beat
              UNION ALL SELECT 'entry ' || id || coalesce(summary, '~') || body || aliases::text || (archived_at IS NULL)::text FROM kb_entry
              UNION ALL SELECT 'scene_kb ' || scene_id || kb_entry_id FROM scene_kb WHERE archived_at IS NULL
              UNION ALL SELECT 'beat_kb ' || beat_id || kb_entry_id FROM beat_kb WHERE archived_at IS NULL
              UNION ALL SELECT 'note ' || id || kb_entry_id || (archived_at IS NULL)::text FROM kb_progression
              UNION ALL SELECT 'ref ' || id || coalesce(kb_entry_id::text, '~') FROM reference
              UNION ALL SELECT 'link ' || entry_a_id || entry_b_id FROM kb_entry_link WHERE archived_at IS NULL
            ) t
            """))!;
    }
}
