using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Manuscript;

// A failure after the prose row is written rolls back the whole edit, including what the prose triggers did.
// The failure is a trigger this test adds to its own database: prose_revision rejects a body containing FAIL_REV,
// so the prose upsert (and its embed sync, which archives the beat whose line went missing) runs first and the
// revision insert then fails inside the same transaction.
[TestFixture]
public class ProseStoreRollbackTests : SchemaApiFixture
{
    private Guid _sceneId;
    private Guid _beatId;

    private IProseStore Store => Host.Services.GetRequiredService<IProseStore>();

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        _beatId = await TestRows.BeatAsync(db, _sceneId);
        await TestRows.ProseAsync(db, _sceneId, $"::beat{{id=\"{_beatId}\"}}\n\nThe beat's prose.\n");
        await db.ExecuteAsync(
            """
            CREATE FUNCTION test_fail_revision() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'revision rejected by the test'; END $$;
            CREATE TRIGGER test_fail_revision BEFORE INSERT ON prose_revision
                FOR EACH ROW WHEN (NEW.body LIKE '%FAIL_REV%') EXECUTE FUNCTION test_fail_revision();
            """);
    }

    [Test]
    [Order(1)]
    public async Task Update_WhenAWriteAfterTheProseFails_ShouldRollBackTheProseAndItsEmbedSync()
    {
        var thrown = await Should.ThrowAsync<ApiException>(() => Store.UpdateAsync(_sceneId, _ => "FAIL_REV, and no beat line.\n", 1, "manual"));

        thrown.Code.ShouldBe(ApiErrorCodes.Validation);
        await using var db = await OpenDatabaseAsync();
        var prose = await Store.GetAsync(_sceneId);
        (prose.Version, prose.Body.Contains(_beatId.ToString(), StringComparison.Ordinal)).ShouldBe((1, true));
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM beat WHERE id = @_beatId", new { _beatId })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE body LIKE '%FAIL_REV%'")).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task Update_WhenNothingFails_ShouldArchiveTheBeatWhoseLineWentMissing()
    {
        (await Store.UpdateAsync(_sceneId, _ => "No beat line now.\n", 1, "manual")).ShouldBe(2);

        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM beat WHERE id = @_beatId", new { _beatId })).ShouldBeFalse();
    }
}
