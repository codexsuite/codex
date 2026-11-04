using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Npgsql;

namespace Codex.WebApi.Tests.Print;

// GET /api/figures/{id}: the row behind a `::figure` line, for Write's card.
[TestFixture]
public class FigureEndpointTests : AssetApiFixture
{
    private static readonly Guid Figure = SeedIds.Figures.PlayersGuideFigure;

    [Test, Order(1)]
    public async Task Get_WhenTheSeedFigure_ShouldCarryCaptionAndALowDpi()
    {
        var figure = (await Client.GetFromJsonAsync<FigureDto>($"/api/figures/{Figure}"))!;

        (figure.AssetId, figure.Placement, figure.Missing).ShouldBe((SeedIds.Assets.PlayersGuideFigure, "column", false));
        figure.Caption.ShouldBe("The Fallen World, as the players first see it.");
        figure.WebUrl.ShouldBeNull(); // the seed row has no renditions
        figure.Dpi!.Status.ShouldBe("low");
        figure.Dpi.EffectiveDpi.ShouldBeLessThan(figure.Dpi.TargetDpi);
    }

    [Test, Order(2)]
    public async Task Get_WhenTheAssetIsArchived_ShouldBeMissing()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var command = new NpgsqlCommand("UPDATE asset SET archived_at = now() WHERE id = @id", connection);
        command.Parameters.AddWithValue("@id", SeedIds.Assets.PlayersGuideFigure);
        await command.ExecuteNonQueryAsync();

        var figure = (await Client.GetFromJsonAsync<FigureDto>($"/api/figures/{Figure}"))!;

        figure.Missing.ShouldBeTrue();
        figure.WebUrl.ShouldBeNull();
        figure.Dpi.ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task Get_WhenUnknown_ShouldBeNotFound()
    {
        var response = await Client.GetAsync($"/api/figures/{Guid.NewGuid()}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
