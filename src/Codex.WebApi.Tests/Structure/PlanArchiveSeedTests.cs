using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

// Archiving and restoring a seeded scene: the Velocity Trap is quoted in chapter 8 scene 1.
[TestFixture]
public class PlanArchiveSeedTests : ApiFixture
{
    [Test]
    public async Task Restore_WhenSourceWasSignedOffWhileArchived_ShouldReopenItsUse()
    {
        var archived = await Client.PostAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/archive", null);
        var result = (await archived.Content.ReadFromJsonAsync<ArchiveSceneResult>(JsonSerializerOptions.Web))!;
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync(
            "UPDATE reference SET signed_off_at = now(), signed_off_by = @user WHERE id = @id",
            new { id = SeedIds.References.VelocityTrap, user = SeedIds.Users.Jd });

        await Client.PostAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/restore", null);

        result.ArchivedUses.ShouldBe([new SceneUseDto(SeedIds.ReferenceUses.VelocityTrapCh8Sc1, SeedIds.References.VelocityTrap, "The Velocity Trap")]);
        var reopened = await db.ExecuteScalarAsync<bool>(
            """
            SELECT u.changed_at > r.signed_off_at
            FROM reference_use u JOIN reference r ON r.id = u.reference_id WHERE u.id = @id
            """,
            new { id = SeedIds.ReferenceUses.VelocityTrapCh8Sc1 });
        reopened.ShouldBeTrue();
    }
}
