using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Ai;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

// J23 on the seed: Kent Beck (a mention in Ch 3 Sc 1), "teams" (plain in Ch 3 Sc 1, inside the Conway paraphrase in
// Ch 9 Sc 1), a summary, a pending AI draft, and Undo.
[TestFixture]
public class FindReplaceTests : ApiFixture
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Ch3Sc1 = SeedIds.Scenes.Ch3Sc1;
    private static readonly Guid Ch9Sc1 = SeedIds.Scenes.Ch9Sc1;

    private string _beforeCh3Sc1 = "";
    private ReplaceUndoDto _undo = new([], [], []);

    private async Task<T> PostAsync<T>(string path, object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = await Client.PostAsJsonAsync(path, body);
        response.StatusCode.ShouldBe(status);
        return await DraftStreams.ReadAsync<T>(response);
    }

    private Task<FindResultDto> FindAsync(FindRequest request) => PostAsync<FindResultDto>($"/api/books/{Book}/find", request);

    private async Task<string> BodyAsync(Guid scene)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", new { scene }) ?? "";
    }

    private async Task<int> RevisionsAsync(Guid scene)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @scene", new { scene });
    }

    [Test, Order(1)]
    public async Task Find_WhenKentBeck_ShouldFindTheMentionLabelInCh3Sc1()
    {
        var result = await FindAsync(new FindRequest("Kent Beck"));

        var item = result.Items.ShouldHaveSingleItem();
        (item.SceneId, item.Source, item.Status).ShouldBe((Ch3Sc1, "prose", "replaceable"));
        (result.Matches, result.Replaceable, result.Scenes).ShouldBe((1, 1, 1));
    }

    [Test, Order(2)]
    public async Task Replace_WhenKentBeck_ShouldRenameTheMentionKeepItsTargetAndCutARevision()
    {
        _beforeCh3Sc1 = await BodyAsync(Ch3Sc1);
        var revisions = await RevisionsAsync(Ch3Sc1);
        await using var db = await OpenDatabaseAsync();
        var words = await db.ExecuteScalarAsync<int>("SELECT markdown_word_count(@_beforeCh3Sc1)", new { _beforeCh3Sc1 });

        var result = await PostAsync<ReplaceResultDto>($"/api/books/{Book}/replace", new ReplaceRequest("Kent Beck", "Kenneth Beck"));

        (result.Replaced, result.Scenes).ShouldBe((1, 1));
        var body = await BodyAsync(Ch3Sc1);
        body.ShouldContain($"[Kenneth Beck](kb:{SeedIds.KbEntries.KentBeck})");
        (await db.ExecuteScalarAsync<int>("SELECT markdown_word_count(@body)", new { body })).ShouldBe(words);
        (await RevisionsAsync(Ch3Sc1)).ShouldBeGreaterThan(revisions);
        (await db.ExecuteScalarAsync<string>("SELECT name FROM kb_entry WHERE id = @id", new { id = SeedIds.KbEntries.KentBeck })).ShouldBe("Kent Beck");
        _undo = result.Undo;
    }

    [Test, Order(3)]
    public async Task Undo_ShouldPutTheSceneBackAsOneAction()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{Book}/replace/undo", _undo);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(Ch3Sc1)).ShouldBe(_beforeCh3Sc1);
    }

    [Test, Order(4)]
    public async Task Replace_WhenTheOnlyMatchesAreInsideACitedPassage_ShouldChangeNothingAndCutNoRevision()
    {
        var before = await BodyAsync(Ch9Sc1);
        var revisions = await RevisionsAsync(Ch9Sc1);

        var found = await FindAsync(new FindRequest("teams", "chapter", SeedIds.Chapters.Ch9, WholeWord: true));
        var result = await PostAsync<ReplaceResultDto>($"/api/books/{Book}/replace", new ReplaceRequest("teams", "groups", "chapter", SeedIds.Chapters.Ch9, WholeWord: true));

        (found.Matches, found.Cited, found.Replaceable).ShouldBe((2, 2, 0));
        (result.Replaced, result.SkippedCited).ShouldBe((0, 2));
        (await BodyAsync(Ch9Sc1)).ShouldBe(before);
        (await RevisionsAsync(Ch9Sc1)).ShouldBe(revisions);
    }

    [Test, Order(5)]
    public async Task Find_WhenTeamsOverTheFullManuscript_ShouldCountTheLockedAndTheOutsideMatchesLikeTheBodies()
    {
        await using var db = await OpenDatabaseAsync();
        var bodies = await db.QueryAsync<string>(
            "SELECT p.body FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @book AND s.archived_at IS NULL", new { book = SeedIds.Books.ProductEngineering });
        var expected = bodies.Sum(b => Regex.Matches(b, @"\bteams\b", RegexOptions.IgnoreCase).Count);

        var found = await FindAsync(new FindRequest("teams", WholeWord: true));

        found.Matches.ShouldBe(expected);
        // Locked: the Beck paraphrase in Ch 3 Sc 1 and the Conway paraphrase in Ch 9 Sc 1 (twice).
        found.Cited.ShouldBe(3);
        found.Items.Count(i => i.SceneId == Ch3Sc1 && i.Status == "cited").ShouldBe(1);
        found.Replaceable.ShouldBe(expected - 3);
    }

    [Test, Order(6)]
    public async Task Replace_WhenOnlyOneMatchIsNamed_ShouldChangeJustThatOne()
    {
        var found = await FindAsync(new FindRequest("teams", WholeWord: true));
        var target = found.Items.First(i => i.Status == "replaceable");
        var before = await BodyAsync(target.SceneId);

        var result = await PostAsync<ReplaceResultDto>(
            $"/api/books/{Book}/replace", new ReplaceRequest("teams", "groups", WholeWord: true, Only: new ReplaceTarget(target.SceneId, target.Source, target.Ordinal)));

        result.Replaced.ShouldBe(1);
        (await BodyAsync(target.SceneId)).ShouldNotBe(before);
        (await FindAsync(new FindRequest("teams", WholeWord: true))).Replaceable.ShouldBe(found.Replaceable - 1);
    }

    [Test, Order(7)]
    public async Task Replace_WhenSummariesAreOn_ShouldChangeTheSummaryAndLeaveTheMarker()
    {
        var before = await BodyAsync(Ch9Sc1);
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE scene SET summary = 'We design the teams first.' WHERE id = @Ch9Sc1", new { Ch9Sc1 });

        var result = await PostAsync<ReplaceResultDto>(
            $"/api/books/{Book}/replace", new ReplaceRequest("design the teams", "shape the groups", "chapter", SeedIds.Chapters.Ch9, Prose: false, Summaries: true));

        result.Replaced.ShouldBe(1);
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @Ch9Sc1", new { Ch9Sc1 })).ShouldBe("We shape the groups first.");
        (await BodyAsync(Ch9Sc1)).ShouldBe(before);
        result.Undo.Summaries.ShouldHaveSingleItem().Text.ShouldBe("We design the teams first.");
    }

    [Test, Order(8)]
    public async Task Replace_WhenAPendingDraftHoldsMatches_ShouldCountThemAndLeaveTheDraftAlone()
    {
        await using var db = await OpenDatabaseAsync();
        var output = await db.ExecuteScalarAsync<string>("SELECT output FROM ai_generation WHERE id = @id", new { id = SeedIds.AiGenerations.Ch5Sc2Pending }) ?? "";
        var word = Regex.Match(output, @"\b[A-Za-z]{6,}\b").Value;

        var found = await FindAsync(new FindRequest(word, "scene", SeedIds.Scenes.Ch5Sc2, WholeWord: true));
        var result = await PostAsync<ReplaceResultDto>($"/api/books/{Book}/replace", new ReplaceRequest(word, "Changed", "scene", SeedIds.Scenes.Ch5Sc2, WholeWord: true));

        found.InDrafts.ShouldBeGreaterThan(0);
        (result.Replaced, result.InDrafts).ShouldBe((0, found.InDrafts));
        (await db.ExecuteScalarAsync<string>("SELECT output FROM ai_generation WHERE id = @id", new { id = SeedIds.AiGenerations.Ch5Sc2Pending })).ShouldBe(output);
    }

    [Test, Order(9)]
    public async Task Find_WhenQueryEmptyOrReplacementUnchanged_ShouldBe400()
    {
        var empty = await Client.PostAsJsonAsync($"/api/books/{Book}/find", new FindRequest(""));
        var same = await Client.PostAsJsonAsync($"/api/books/{Book}/replace", new ReplaceRequest("teams", "teams"));

        (await empty.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await same.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(10)]
    public async Task Replace_WhenBeatsAreOn_ShouldChangeTheBeatInstructionAndUndoRestoreIt()
    {
        await using var db = await OpenDatabaseAsync();
        var beat = SeedIds.Beats.Ch3Sc3BadScrum;
        var before = await db.ExecuteScalarAsync<string>("SELECT body FROM beat WHERE id = @beat", new { beat });

        var result = await PostAsync<ReplaceResultDto>(
            $"/api/books/{Book}/replace", new ReplaceRequest("Bad Scrum", "Cargo-cult Scrum", Prose: false, Beats: true));
        var changed = await db.ExecuteScalarAsync<string>("SELECT body FROM beat WHERE id = @beat", new { beat });
        await Client.PostAsJsonAsync($"/api/books/{Book}/replace/undo", result.Undo);

        result.Replaced.ShouldBe(1);
        changed.ShouldBe("Cargo-cult Scrum.");
        (await db.ExecuteScalarAsync<string>("SELECT body FROM beat WHERE id = @beat", new { beat })).ShouldBe(before);
    }
}
