using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Manuscript;

// ProseWriteOptions.BeforeWrites runs first in the prose write's transaction (it can create a row the new body points
// at); InTransaction runs after the prose rows are written: it sees the new body, and when it throws nothing is saved.
[TestFixture]
public class ProseWriteOptionsTests : SchemaApiFixture
{
    private Guid _scene1;
    private Guid _scene2;

    private IProseStore Store => Host.Services.GetRequiredService<IProseStore>();

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        Guid bookId;
        Guid chapterId;
        (bookId, chapterId, _scene1) = await TestRows.SingleSceneAsync(db);
        _scene2 = await TestRows.SceneAsync(db, bookId, chapterId, "a1");
        await TestRows.ProseAsync(db, _scene1, "One.\n");
        await TestRows.ProseAsync(db, _scene2, "Two.\n");
    }

    [Test]
    [Order(1)]
    public async Task Update_WhenTheInTransactionStepThrows_ShouldSaveNothing()
    {
        var options = new ProseWriteOptions { InTransaction = (_, _, _) => throw ApiException.Conflict("Refused by the step.") };

        var thrown = await Should.ThrowAsync<ApiException>(() => Store.UpdateAsync(_scene1, _ => "Changed.\n", 1, "manual", options));

        thrown.Code.ShouldBe(ApiErrorCodes.Conflict);
        var prose = await Store.GetAsync(_scene1);
        (prose.Version, prose.Body).ShouldBe((1, "One.\n"));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE body = 'Changed.\n'")).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task UpdateMany_WhenTheInTransactionStepThrows_ShouldLeaveEveryScene()
    {
        var options = new ProseWriteOptions { InTransaction = (_, _, _) => throw ApiException.Validation("Refused by the step.") };

        await Should.ThrowAsync<ApiException>(() => Store.UpdateManyAsync(
            [(_scene1, 1, _ => "Changed one.\n"), (_scene2, 1, _ => "Changed two.\n")], "editor", options));

        (await Store.GetAsync(_scene1)).Body.ShouldBe("One.\n");
        (await Store.GetAsync(_scene2)).Body.ShouldBe("Two.\n");
    }

    [Test]
    [Order(3)]
    public async Task Update_WithAStepAndAGenerationId_ShouldSeeTheNewBodyAndTagTheRevision()
    {
        Guid generationId;
        await using (var db = await OpenDatabaseAsync())
        {
            generationId = await db.ExecuteScalarAsync<Guid>(
                """
                INSERT INTO ai_generation (book_id, scene_id, kind, model, request)
                SELECT book_id, id, 'continue', 'test-model', '{}' FROM scene WHERE id = @_scene1 RETURNING id
                """, new { _scene1 });
        }
        string? seen = null;
        var options = new ProseWriteOptions
        {
            GenerationId = generationId,
            InTransaction = async (connection, transaction, ct) => seen = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT body FROM prose WHERE scene_id = @_scene1", new { _scene1 }, transaction, cancellationToken: ct)),
        };

        var version = await Store.UpdateAsync(_scene1, _ => "Applied.\n", 1, "ai_apply", options);

        (version, seen).ShouldBe((2, "Applied.\n"));
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<Guid?>(
            "SELECT generation_id FROM prose_revision WHERE scene_id = @_scene1 AND source = 'ai_apply'", new { _scene1 })).ShouldBe(generationId);
    }

    [Test]
    [Order(4)]
    public async Task Update_WhenBeforeWritesCreatesTheBeatTheBodyNames_ShouldSaveBoth()
    {
        var beatId = Guid.CreateVersion7();
        var options = new ProseWriteOptions
        {
            BeforeWrites = (connection, transaction, ct) => connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO beat (id, scene_id) VALUES (@beatId, @_scene2)", new { beatId, _scene2 }, transaction, cancellationToken: ct)),
        };

        var version = await Store.UpdateAsync(_scene2, body => $"::beat{{id=\"{beatId}\"}}\n\n{body}", 1, "editor", options);

        version.ShouldBe(2);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM beat WHERE id = @beatId", new { beatId })).ShouldBeTrue();
    }
}
