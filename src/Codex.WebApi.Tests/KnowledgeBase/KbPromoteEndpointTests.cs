using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

[TestFixture]
public class KbPromoteEndpointTests : SchemaApiFixture
{
    private async Task<(Guid Series, Guid Book, Guid Category, Guid Entry)> ScenarioAsync(string entryName = "Conway's Law")
    {
        await using var db = await OpenDatabaseAsync();
        var series = await TestRows.SeriesAsync(db, "Editions " + Guid.NewGuid());
        var book = await TestRows.ShelfBookAsync(db, "PE", null, series, 1);
        var laws = await TestRows.CategoryAsync(db, book, "Laws");
        var entry = await TestRows.EntryAsync(db, book, laws, entryName);
        return (series, book, laws, entry);
    }

    private static HttpContent Body(bool merge) => JsonContent.Create(new PromoteKbEntryRequest(merge));

    [Test]
    public async Task Promote_WhenNoClash_ShouldMoveTheEntryToTheSeriesKeepingItsId()
    {
        var (series, _, _, entry) = await ScenarioAsync();

        var response = await Client.PostAsync($"/api/kb/entries/{entry}/promote", Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        dto.Id.ShouldBe(entry);
        dto.BookId.ShouldBe(series);
        dto.CategoryName.ShouldBe("Laws");
    }

    [Test]
    public async Task Promote_WhenSeriesHasSameName_ShouldAnswer409AndWriteNothingUntilMerged()
    {
        var (series, book, _, entry) = await ScenarioAsync();
        Guid existing;
        await using (var db = await OpenDatabaseAsync())
        {
            existing = await TestRows.EntryAsync(db, series, await TestRows.CategoryAsync(db, series, "Laws"), "Conway's Law");
        }

        var refused = await Client.PostAsync($"/api/kb/entries/{entry}/promote", Body(false));

        (await refused.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.NeedsConfirmation);
        await using (var db = await OpenDatabaseAsync())
        {
            (await db.ExecuteScalarAsync<Guid>("SELECT book_id FROM kb_entry WHERE id = @entry", new { entry })).ShouldBe(book);
        }

        var merged = await Client.PostAsync($"/api/kb/entries/{entry}/promote", Body(true));

        merged.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await merged.Content.ReadFromJsonAsync<KbEntryDto>())!.Id.ShouldBe(existing);
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM kb_entry WHERE id = @entry AND archived_at IS NULL", new { entry })).ShouldBe(0);
    }

    [Test]
    public async Task Promote_WhenMergedIntoTheSeriesEntry_ShouldArchiveTheBookEntryAndMoveItsLinks()
    {
        var (series, book, laws, entry) = await ScenarioAsync();
        Guid existing;
        Guid scene;
        Guid beat;
        Guid other;
        await using (var db = await OpenDatabaseAsync())
        {
            existing = await TestRows.EntryAsync(db, series, await TestRows.CategoryAsync(db, series, "Laws"), "Conway's Law");
            other = await TestRows.EntryAsync(db, book, laws, "Brooks's Law", "a1");
            scene = await TestRows.SceneAsync(db, book, await TestRows.ChapterAsync(db, book, await TestRows.FirstActAsync(db, book), "a0", "One"), "a0");
            beat = await TestRows.BeatAsync(db, scene);
            await db.ExecuteAsync("INSERT INTO scene_kb VALUES (@scene, @entry)", new { scene, entry });
            await db.ExecuteAsync("INSERT INTO beat_kb VALUES (@beat, @entry)", new { beat, entry });
            await db.ExecuteAsync("INSERT INTO kb_entry_link VALUES (least(@entry, @other), greatest(@entry, @other))", new { entry, other });
        }

        var merged = await Client.PostAsync($"/api/kb/entries/{entry}/promote", Body(true));

        merged.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var check = await OpenDatabaseAsync();
        // The book's entry is kept, archived, with its links archived; the series entry has them live.
        (await check.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @entry", new { entry })).ShouldBeTrue();
        const string links =
            """
            SELECT (SELECT count(*) FROM scene_kb WHERE kb_entry_id = @id AND scene_id = @scene AND (archived_at IS NULL) = @live)
                 + (SELECT count(*) FROM beat_kb WHERE kb_entry_id = @id AND beat_id = @beat AND (archived_at IS NULL) = @live)
                 + (SELECT count(*) FROM kb_entry_link WHERE entry_a_id = least(@id, @other) AND entry_b_id = greatest(@id, @other)
                      AND (archived_at IS NULL) = @live)
            """;
        (await check.ExecuteScalarAsync<long>(links, new { id = entry, scene, beat, other, live = false })).ShouldBe(3);
        (await check.ExecuteScalarAsync<long>(links, new { id = entry, scene, beat, other, live = true })).ShouldBe(0);
        (await check.ExecuteScalarAsync<long>(links, new { id = existing, scene, beat, other, live = true })).ShouldBe(3);
    }

    [Test]
    public async Task Promote_WhenBookIsStandalone_ShouldAnswer400()
    {
        Guid entry;
        await using (var db = await OpenDatabaseAsync())
        {
            var book = await TestRows.BookAsync(db, "Alone");
            entry = await TestRows.EntryAsync(db, book, await TestRows.CategoryAsync(db, book, "Laws"), "X");
        }

        var response = await Client.PostAsync($"/api/kb/entries/{entry}/promote", Body(false));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Promote_WhenAlreadyInTheSeries_ShouldAnswer409()
    {
        var (series, _, _, _) = await ScenarioAsync();
        Guid seriesEntry;
        await using (var db = await OpenDatabaseAsync())
        {
            seriesEntry = await TestRows.EntryAsync(db, series, await TestRows.CategoryAsync(db, series, "Spells"), "Gravetouch");
        }

        var response = await Client.PostAsync($"/api/kb/entries/{seriesEntry}/promote", Body(false));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }
}
