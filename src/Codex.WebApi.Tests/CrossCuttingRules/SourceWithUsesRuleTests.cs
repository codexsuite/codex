using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.CrossCuttingRules;

// Cross-cutting rule 4: deleting a source converts, never blocks and never hard-deletes (DELETE is the archive). Archiving turns every marker into plain text (the
// quoted words stay), archives the uses and takes the source out of the table, the bibliography and the exports.
// SourceArchiveTests pins the unwrapped prose and the clearance summary; this pins the rest.
[TestFixture]
public class SourceWithUsesRuleTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;
    private static readonly Guid Source = SeedIds.References.VelocityTrap;

    [Test, Order(1)]
    public async Task Delete_WhenTheSourceHasUses_ShouldConvertNotBlock()
    {
        var lyric = SeedIds.References.ShipItFriday;
        await using var db = await OpenDatabaseAsync();
        var revisions = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose_revision WHERE scene_id = @scene", new { scene = SeedIds.Scenes.Ch10Sc1 });
        using var response = await Client.DeleteAsync($"/api/references/{lyric}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference WHERE id = @lyric", new { lyric })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference WHERE id = @lyric", new { lyric })).ShouldBe(1);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_use WHERE reference_id = @lyric AND archived_at IS NULL", new { lyric })).ShouldBe(0);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose_revision WHERE scene_id = @scene", new { scene = SeedIds.Scenes.Ch10Sc1 })).ShouldBe(revisions + 1);
    }

    [Test, Order(2)]
    public async Task Archive_WhenTheSourceHasUses_ShouldArchiveTheUsesAndLeaveBibliographyAndExports()
    {
        using var response = await Client.PostAsync($"/api/references/{Source}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetFromJsonAsync<SourceCslDto[]>($"/api/books/{Book}/sources/csl", JsonSerializerOptions.Web))!
            .Select(r => r.SourceId).ShouldNotContain(Source);
        (await Client.GetStringAsync($"/api/books/{Book}/sources/export?format=bibtex")).ShouldNotContain("Velocity Trap");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_use WHERE reference_id = @Source AND archived_at IS NULL", new { Source })).ShouldBe(0);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_use WHERE reference_id = @Source", new { Source })).ShouldBe(2);
    }
}
