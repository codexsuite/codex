using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Structure;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Manuscript;

// Concurrency rule, documented here: edits are serialised by a row lock on the scene, and every caller names the
// version it read. Of two edits at the same version the first commits and the second fails with a conflict
// (ApiException, code "conflict"): neither is lost silently, and the loser reloads and retries.
[TestFixture]
public class ProseStoreTests : ApiFixture
{
    private Guid _bookId;
    private Guid _chapterId;

    private IProseStore Store => Host.Services.GetRequiredService<IProseStore>();

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _chapterId, _) = await TestRows.SingleSceneAsync(db);
    }

    [Test]
    [Order(1)]
    public async Task Update_WhenConcurrentEdits_ShouldNotLoseEither()
    {
        var sceneId = await NewSceneAsync("start");

        var results = await Task.WhenAll(
            Attempt(() => Store.UpdateAsync(sceneId, body => body + " A", 1, "api")),
            Attempt(() => Store.UpdateAsync(sceneId, body => body + " B", 1, "api")));

        results.Count(r => r == null).ShouldBe(1);
        results.Single(r => r != null)!.Code.ShouldBe(ApiErrorCodes.Conflict);
        var saved = await Store.GetAsync(sceneId);
        saved.Version.ShouldBe(2);
        saved.Body.ShouldBeOneOf("start A", "start B");
    }

    [Test]
    [Order(2)]
    public async Task Update_WhenRetriedAtNewVersion_ShouldApplyBothEdits()
    {
        var sceneId = await NewSceneAsync("start");

        await Store.UpdateAsync(sceneId, body => body + " A", 1, "api");
        await Store.UpdateAsync(sceneId, body => body + " B", 2, "mcp");

        (await Store.GetAsync(sceneId)).Body.ShouldBe("start A B");
    }

    [Test]
    [Order(3)]
    public async Task Update_WhenEditThrows_ShouldRollBack()
    {
        var sceneId = await NewSceneAsync("start");

        await Should.ThrowAsync<InvalidOperationException>(
            () => Store.UpdateAsync(sceneId, _ => throw new InvalidOperationException("boom"), 1, "api"));

        var saved = await Store.GetAsync(sceneId);
        (saved.Body, saved.Version).ShouldBe(("start", 1));
        // Nothing half-written, and the scene row lock was released: a following edit goes through.
        (await Store.UpdateAsync(sceneId, body => body + "!", 1, "api")).ShouldBe(2);
    }

    [Test]
    [Order(4)]
    public async Task Update_WhenOriginIsAiApply_ShouldCutRevisionOfThatSource()
    {
        var sceneId = await NewSceneAsync("start");

        await Store.UpdateAsync(sceneId, body => body + " generated", 1, "ai_apply");

        await using var db = await OpenDatabaseAsync();
        (await db.QuerySingleAsync<(string Source, string Body)>(
            "SELECT source, body FROM prose_revision WHERE scene_id = @sceneId", new { sceneId })).ShouldBe(("ai_apply", "start generated"));
    }

    [Test]
    [Order(5)]
    public async Task Update_WhenEmbedIsInvalid_ShouldFailWithValidationAndSaveNothing()
    {
        var sceneId = await NewSceneAsync("start");
        var strayBeat = $"::beat{{id=\"{Guid.NewGuid()}\"}}";

        var error = await Should.ThrowAsync<ApiException>(() => Store.UpdateAsync(sceneId, body => $"{body}\n{strayBeat}\n", 1, "api"));

        error.Code.ShouldBe(ApiErrorCodes.Validation);
        (await Store.GetAsync(sceneId)).Version.ShouldBe(1);
    }

    [Test]
    [Order(6)]
    public async Task Update_WhenUnknownScene_ShouldThrowNotFound()
    {
        var error = await Should.ThrowAsync<ApiException>(() => Store.UpdateAsync(Guid.NewGuid(), b => b, 0, "api"));

        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<Guid> NewSceneAsync(string body)
    {
        await using var db = await OpenDatabaseAsync();
        var sceneId = await TestRows.SceneAsync(db, _bookId, _chapterId, $"b{Guid.NewGuid():N}");
        await TestRows.ProseAsync(db, sceneId, body);
        return sceneId;
    }

    private static async Task<ApiException?> Attempt(Func<Task<int>> update)
    {
        try
        {
            await update();
            return null;
        }
        catch (ApiException e)
        {
            return e;
        }
    }
}
