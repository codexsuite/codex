using System.Net.Http.Json;
using Codex.Contracts.References;
using Codex.WebApi.Tests.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.References;

[TestFixture]
public class MarkerEndpointTests : BookContentFixture
{
    private Guid _book;
    private Guid _sceneOne;
    private Guid _risky;
    private Guid _cleared;
    private Guid _unsigned;
    private Guid _riskyUse;
    private Guid _clearedUse;
    private Guid _unsignedUse;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        _book = await AddBookAsync("Tides");
        await SetQuoteLimitAsync(_book, 3);
        var chapter = await AddChapterAsync(_book);
        _sceneOne = await AddSceneAsync(_book, chapter, "a0");
        var sceneTwo = await AddSceneAsync(_book, chapter, "a1");
        _risky = await AddSourceAsync(_book, "Risky", "all_rights_reserved");
        _cleared = await AddSourceAsync(_book, "Cleared");
        _unsigned = await AddSourceAsync(_book, "Unsigned");
        _riskyUse = await AddUseAsync(_book, _risky, _sceneOne, "five words are quoted here");
        _clearedUse = await AddUseAsync(_book, _cleared, _sceneOne, "two words", "paraphrase");
        _unsignedUse = await AddUseAsync(_book, _unsigned, sceneTwo, "one", "paraphrase");
        await SetProseAsync(
            _sceneOne,
            $"As [five words are quoted here](ref:{_riskyUse}) and [two words](ref:{_clearedUse}) say.");
        await SetProseAsync(sceneTwo, $"Later [one](ref:{_unsignedUse}).");
        await SignOffAsync(_cleared);
        await SignOffAsync(_risky);
    }

    [Test, Order(1)]
    public async Task Get_ShouldNumberInReadingOrderWithStatus()
    {
        var markers = (await Client.GetFromJsonAsync<List<MarkerDto>>($"/api/books/{_book}/markers"))!;

        markers.Select(m => m.UseId).ShouldBe([_riskyUse, _clearedUse, _unsignedUse]);
        markers.Select(m => m.Number).ShouldBe([1, 2, 3]);
        markers.Select(m => m.Status).ShouldBe(["risk", "cleared", "needs_check"]);
        markers.Select(m => m.SourceTitle).ShouldBe(["Risky", "Cleared", "Unsigned"]);
        markers.Select(m => m.SourceId).ShouldBe([_risky, _cleared, _unsigned]);
        markers[0].Reason.ShouldBe("quote_over_limit");
        markers[1].Reason.ShouldBe("signed_off");
        markers[2].Reason.ShouldBe("ready_to_sign_off");
        markers[0].QuotedWords.ShouldBe(5);
        markers[0].Limit.ShouldBe(3);
        markers[0].Kind.ShouldBe("quote");
        markers.ShouldAllBe(m => m.ChapterNumber == 1);
        markers.Select(m => m.SceneNumber).ShouldBe([1, 1, 2]);
        markers.ShouldAllBe(m => m.GenerationId == null);
    }

    [Test, Order(2)]
    public async Task Get_WhenPendingDraft_ShouldCarryGenerationId()
    {
        var draftSource = await AddSourceAsync(_book, "Draft");
        Guid beat;
        Guid generation;
        Guid draftUse;
        await using (var connection = await OpenDatabaseAsync())
        {
            beat = await connection.QuerySingleAsync<Guid>(
                "INSERT INTO beat (scene_id, body) VALUES (@scene, 'beat') RETURNING id", new { scene = _sceneOne });
            generation = await connection.QuerySingleAsync<Guid>(
                "INSERT INTO ai_generation (book_id, scene_id, beat_id, kind, model, request, status) VALUES (@book, @scene, @beat, 'beat_prose', 'm', '{}', 'pending') RETURNING id",
                new { book = _book, scene = _sceneOne, beat });
        }
        draftUse = await AddUseAsync(_book, draftSource, _sceneOne, "draft", "paraphrase", generation);
        await using (var connection = await OpenDatabaseAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE ai_generation SET output = @output WHERE id = @generation",
                new { generation, output = $"Drafted [draft](ref:{draftUse})." });
        }
        // the beat line sits between the two prose markers
        await SetProseAsync(
            _sceneOne,
            $"As [five words are quoted here](ref:{_riskyUse}).\n\n::beat{{id=\"{beat}\"}}\n\nAnd [two words](ref:{_clearedUse}) say.");

        var markers = (await Client.GetFromJsonAsync<List<MarkerDto>>($"/api/books/{_book}/markers"))!;

        markers.Select(m => m.UseId).ShouldBe([_riskyUse, draftUse, _clearedUse, _unsignedUse]);
        markers[1].GenerationId.ShouldBe(generation);
        markers[1].SourceTitle.ShouldBe("Draft");
        markers.Where(m => m.UseId != draftUse).ShouldAllBe(m => m.GenerationId == null);
    }

    [Test, Order(3)]
    public async Task Get_WhenUnknownBook_ShouldReturnNotFound()
    {
        (await ApiErrors.ReadApiErrorAsync(await Client.GetAsync($"/api/books/{Guid.NewGuid()}/markers"), System.Net.HttpStatusCode.NotFound))
            .Code.ShouldBe("not_found");
    }
}
