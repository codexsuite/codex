using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

// J15 step 9: a source use that comes back after the source was signed off while it was away counts as new, so the
// sign-off reopens; one restored with no sign-off in between is left as it was.
[TestFixture]
public class ReferenceUseRestoreSignoffTests : ApiFixture
{
    [Test]
    public async Task Unarchive_WhenSignedOffWhileArchived_ShouldStampChangedAt()
    {
        await using var db = await OpenDatabaseAsync();
        var (bookId, _, sceneId) = await TestRows.SingleSceneAsync(db);
        var source = await TestRows.SourceAsync(db, bookId, "Reopened");
        var quiet = await TestRows.SourceAsync(db, bookId, "Quiet");
        var reopened = await TestRows.UseAsync(db, bookId, source, sceneId, "text");
        var untouched = await TestRows.UseAsync(db, bookId, quiet, sceneId, "text");
        await db.ExecuteAsync("UPDATE reference_use SET archived_at = now() - interval '1 hour', changed_at = now() - interval '2 hours'");
        await db.ExecuteAsync("UPDATE reference SET signed_off_at = now() - interval '30 minutes' WHERE id = @source", new { source });

        await db.ExecuteAsync("UPDATE reference_use SET archived_at = NULL");

        (await db.ExecuteScalarAsync<bool>("SELECT changed_at > now() - interval '1 minute' FROM reference_use WHERE id = @reopened", new { reopened })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<bool>("SELECT changed_at < now() - interval '1 hour' FROM reference_use WHERE id = @untouched", new { untouched })).ShouldBeTrue();
    }
}
