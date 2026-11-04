using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

[TestFixture]
public class BeatContextTests : BookContentFixture
{
    private Guid _beat;
    private Guid _a;
    private Guid _b;
    private Guid _foreign;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var book = await AddBookAsync("Tides");
        var other = await AddBookAsync("Elsewhere");
        var category = await AddCategoryAsync(book, "People");
        _a = await AddEntryAsync(book, category, "Anna");
        _b = await AddEntryAsync(book, category, "Ben");
        _foreign = await AddEntryAsync(other, await AddCategoryAsync(other, "People"), "Stranger");
        _beat = await AddBeatAsync(await AddSceneAsync(book, await AddChapterAsync(book)));
    }

    [Test, Order(1)]
    public async Task Put_ShouldReplaceSet()
    {
        (await Client.PutAsJsonAsync($"/api/beats/{_beat}/context", new SetBeatContextRequest([_a, _a]))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await ContextAsync()).ShouldBe([_a]);

        (await Client.PutAsJsonAsync($"/api/beats/{_beat}/context", new SetBeatContextRequest([_b]))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await ContextAsync()).ShouldBe([_b]);
        (await ArchivedAsync()).ShouldBe([_a]);

        (await Client.PutAsJsonAsync($"/api/beats/{_beat}/context", new SetBeatContextRequest([]))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await ContextAsync()).ShouldBeEmpty();
        (await ArchivedAsync()).OrderBy(id => id).ShouldBe(new[] { _a, _b }.OrderBy(id => id));
    }

    [Test, Order(2)]
    public async Task Put_WhenEntryFromOtherBook_ShouldRejectAndKeepSet()
    {
        await Client.PutAsJsonAsync($"/api/beats/{_beat}/context", new SetBeatContextRequest([_a]));

        var error = await ApiErrors.ReadApiErrorAsync(
            await Client.PutAsJsonAsync($"/api/beats/{_beat}/context", new SetBeatContextRequest([_b, _foreign])),
            HttpStatusCode.BadRequest);

        error.Code.ShouldBe("validation");
        (await ContextAsync()).ShouldBe([_a]);
    }

    [Test, Order(3)]
    public async Task Put_WhenUnknownBeat_ShouldReturnNotFound()
    {
        (await ApiErrors.ReadApiErrorAsync(
            await Client.PutAsJsonAsync($"/api/beats/{Guid.NewGuid()}/context", new SetBeatContextRequest([_a])),
            HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(4)]
    public async Task Put_WhenEntryAddedAgain_ShouldReviveItsArchivedRow()
    {
        (await Client.PutAsJsonAsync($"/api/beats/{_beat}/context", new SetBeatContextRequest([_a, _b]))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await ContextAsync()).OrderBy(id => id).ShouldBe(new[] { _a, _b }.OrderBy(id => id));
        (await ArchivedAsync()).ShouldBeEmpty();
        await using var connection = await OpenDatabaseAsync();
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM beat_kb WHERE beat_id = @b", new { b = _beat })).ShouldBe(2);
    }

    private async Task<Guid[]> ContextAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        return (await connection.QueryAsync<Guid>(
            "SELECT kb_entry_id FROM beat_kb WHERE beat_id = @b AND archived_at IS NULL", new { b = _beat })).ToArray();
    }

    // Context entries dropped from the beat: their rows are kept, archived.
    private async Task<Guid[]> ArchivedAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        return (await connection.QueryAsync<Guid>(
            "SELECT kb_entry_id FROM beat_kb WHERE beat_id = @b AND archived_at IS NOT NULL", new { b = _beat })).ToArray();
    }
}
