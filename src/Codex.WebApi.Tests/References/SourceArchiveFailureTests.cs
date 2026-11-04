using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.References;

// The archive step runs in the prose write's transaction: when archiving the source fails after the scenes were
// unwrapped, the scenes and the uses stay as they were.
[TestFixture]
public class SourceArchiveFailureTests : SourceApiFixture
{
    private string _body = "";

    [OneTimeSetUp]
    public async Task RefuseArchiveAsync()
    {
        _body = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch4Sc1}/prose"))!.Body;
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync(
            """
            CREATE FUNCTION refuse_source_archive() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN IF NEW.archived_at IS NOT NULL THEN RAISE EXCEPTION 'archive refused'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER refuse_source_archive BEFORE UPDATE ON reference FOR EACH ROW EXECUTE FUNCTION refuse_source_archive();
            """);
    }

    [Test]
    public async Task Archive_WhenArchivingTheSourceFails_ShouldLeaveTheProseAndTheUsesAsTheyWere()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.VelocityTrap}/archive", null);

        (await ApiErrors.ReadApiErrorAsync(response, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch4Sc1}/prose"))!;
        prose.Body.ShouldBe(_body);
        prose.Body.ShouldContain(SeedIds.ReferenceUses.VelocityTrapCh4Sc1.ToString());
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_use WHERE reference_id = @id AND archived_at IS NULL", new { id = SeedIds.References.VelocityTrap }))
            .ShouldBe(2);
    }
}
