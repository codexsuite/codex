using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Dapper;

namespace Codex.WebApi.Tests.Ai;

// Apply writes the prose, tags its revision and marks the draft applied in one transaction: when marking the draft
// fails, the scene keeps its body and no ai_apply revision is left behind.
[TestFixture]
public class DraftApplyAtomicityTests : SchemaApiFixture
{
    private Guid _sceneId;
    private Guid _generationId;
    private const string Body = "Opening line.\n";

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneId, Body);
        _generationId = await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ai_generation (book_id, scene_id, kind, model, request, output)
            SELECT book_id, id, 'continue', 'test-model', '{}', 'The continuation.' FROM scene WHERE id = @_sceneId RETURNING id
            """, new { _sceneId });
        await db.ExecuteAsync(
            """
            CREATE FUNCTION refuse_applied() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN IF NEW.status = 'applied' THEN RAISE EXCEPTION 'apply refused'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER refuse_applied BEFORE UPDATE ON ai_generation FOR EACH ROW EXECUTE FUNCTION refuse_applied();
            """);
    }

    [Test, Order(1)]
    public async Task Apply_WhenMarkingTheDraftFails_ShouldLeaveTheSceneAndCutNoRevision()
    {
        var response = await Client.PostAsJsonAsync($"/api/generations/{_generationId}/apply", new DraftVersionRequest(1));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        await using var db = await OpenDatabaseAsync();
        (await db.QuerySingleAsync<(string Body, int Version)>("SELECT body, version FROM prose WHERE scene_id = @_sceneId", new { _sceneId }))
            .ShouldBe((Body, 1));
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @_sceneId AND source = 'ai_apply'", new { _sceneId }))
            .ShouldBe(0);
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @_generationId", new { _generationId })).ShouldBe("pending");
    }

    [Test, Order(2)]
    public async Task Apply_WhenNothingFails_ShouldTagTheRevisionWithTheGeneration()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("DROP TRIGGER refuse_applied ON ai_generation");
        }

        var response = await Client.PostAsJsonAsync($"/api/generations/{_generationId}/apply", new DraftVersionRequest(1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<Guid?>(
            "SELECT generation_id FROM prose_revision WHERE scene_id = @_sceneId AND source = 'ai_apply'", new { _sceneId })).ShouldBe(_generationId);
        (await check.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @_generationId", new { _generationId })).ShouldBe("applied");
    }
}
