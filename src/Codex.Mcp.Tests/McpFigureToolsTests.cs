using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// place_figure, update_figure and set_opener (#65) over stdio on the Players Guide's raw-LaTeX scene, with the seed's
// map image: a stale version writes nothing, a figure is placed, changed, removed and put back by undo, undoing the
// placement takes its line out and archives it, and an act opener is set and undone. One scenario on its own clone.
[TestFixture]
[CancelAfter(60000)]
public class McpFigureToolsTests
{
    private static readonly Guid Scene = SeedIds.Scenes.PlayersGuideLatexScene;
    private static readonly string Asset = SeedIds.Assets.PlayersGuideFigure.ToString();
    private static readonly string PlayersGuide = SeedIds.Books.PlayersGuide.ToString();

    private McpStdioServer _server = null!;
    private string _body = "";
    private int _version;
    private Guid _figure;
    private string _placeUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        (_body, _version) = await ProseAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test, Order(1)]
    public async Task PlaceFigure_WhenTheVersionIsStale_ShouldConflictAndWriteNothing()
    {
        var error = await Client.ErrorAsync("place_figure", new { scene = Scene.ToString(), at = "end", asset = Asset, expected_version = _version - 1 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ProseAsync()).ShouldBe((_body, _version));
        (await FiguresInSceneAsync()).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task PlaceFigure_WhenThePlacementIsUnknown_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("place_figure", new
        {
            scene = Scene.ToString(), at = "end", asset = Asset, expected_version = _version, placement = "sidebar",
        });

        (error.GetProperty("code").GetString(), error.GetProperty("field").GetString()).ShouldBe(("invalid", "placement"));
    }

    [Test, Order(3)]
    public async Task PlaceFigure_WhenValid_ShouldWriteTheFigureAndItsLine()
    {
        var result = await Client.OkAsync("place_figure", new
        {
            scene = Scene.ToString(), at = "end", asset = Asset, expected_version = _version, placement = "page", caption = "The map", alt_text = "A map",
        });

        _figure = Guid.Parse(result.GetProperty("figure").GetProperty("id").GetString()!);
        _placeUndo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("version").GetInt32().ShouldBe(_version + 1);
        result.GetProperty("placement").GetString().ShouldBe("page");
        (await ProseAsync()).Body.ShouldEndWith($"::figure{{id=\"{_figure}\"}}\n");
        (await FigureAsync()).ShouldBe(new FigureRow(false, "page", "The map", "A map"));
    }

    [Test, Order(4)]
    public async Task UpdateFigure_WhenUndone_ShouldPutTheCaptionAndAltTextBack()
    {
        var update = await Client.OkAsync("update_figure", new { figure = _figure, caption = "A new caption", alt_text = "" });
        (await FigureAsync()).ShouldBe(new FigureRow(false, "page", "A new caption", null));

        await Client.OkAsync("undo", new { undo_id = update.GetProperty("undo_id").GetString() });

        (await FigureAsync()).ShouldBe(new FigureRow(false, "page", "The map", "A map"));
    }

    [Test, Order(5)]
    public async Task UpdateFigure_WhenRemovingWithoutAVersion_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("update_figure", new { figure = _figure, remove = true });

        (error.GetProperty("code").GetString(), error.GetProperty("field").GetString()).ShouldBe(("invalid", "expected_version"));
    }

    [Test, Order(6)]
    public async Task UpdateFigure_WhenRemovedThenUndone_ShouldArchiveItThenBringItBack()
    {
        var (_, version) = await ProseAsync();

        var remove = await Client.OkAsync("update_figure", new { figure = _figure, remove = true, expected_version = version });

        (await ProseAsync()).Body.ShouldNotContain(_figure.ToString());
        (await FigureAsync()).Archived.ShouldBeTrue();

        await Client.OkAsync("undo", new { undo_id = remove.GetProperty("undo_id").GetString() });

        (await ProseAsync()).Body.ShouldContain(_figure.ToString());
        (await FigureAsync()).Archived.ShouldBeFalse();
    }

    [Test, Order(7)]
    public async Task Undo_WhenThePlacedSceneWasEditedSince_ShouldConflictAndUndoNothing()
    {
        var error = await Client.ErrorAsync("undo", new { undo_id = _placeUndo });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await FigureAsync()).Archived.ShouldBeFalse();
    }

    [Test, Order(8)]
    public async Task Undo_WhenAFigureWasJustPlaced_ShouldTakeTheLineOutAndArchiveIt()
    {
        var before = await ProseAsync();
        var placed = await Client.OkAsync("place_figure", new { scene = Scene.ToString(), at = "start", asset = Asset, expected_version = before.Version });
        var figure = Guid.Parse(placed.GetProperty("figure").GetProperty("id").GetString()!);

        await Client.OkAsync("undo", new { undo_id = placed.GetProperty("undo_id").GetString() });

        (await ProseAsync()).Body.ShouldBe(before.Body);
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM figure WHERE id = @figure", new { figure })).ShouldBeTrue();
    }

    [Test, Order(9)]
    public async Task SetOpener_WhenSetThenUndone_ShouldStoreThenClearTheActsArt()
    {
        var result = await Client.OkAsync("set_opener", new { target = "act:1", book = PlayersGuide, asset = Asset, caption = "Part one" });

        result.GetProperty("item").GetProperty("id").GetString().ShouldBe(SeedIds.Acts.PlayersGuideAct1.ToString());
        (await OpenerAsync()).ShouldBe((SeedIds.Assets.PlayersGuideFigure, "Part one"));

        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        (await OpenerAsync()).ShouldBe(((Guid?)null, (string?)null));
    }

    [Test, Order(10)]
    public async Task SetOpener_WhenTheTargetIsAScene_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("set_opener", new { target = "1.1", book = PlayersGuide, asset = Asset });

        (error.GetProperty("code").GetString(), error.GetProperty("field").GetString()).ShouldBe(("invalid", "target"));
    }

    [Test, Order(11)]
    public async Task SetOpener_WhenTheImageIsAnotherBooks_ShouldBeInvalidAndWriteNothing()
    {
        var error = await Client.ErrorAsync("set_opener", new { target = SeedIds.Chapters.Ch3.ToString(), asset = Asset });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<Guid?>("SELECT opener_asset_id FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.Ch3 })).ShouldBeNull();
    }

    public sealed record FigureRow(bool Archived, string Placement, string? Caption, string? AltText);

    private async Task<FigureRow> FigureAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<FigureRow>(
            "SELECT archived_at IS NOT NULL AS Archived, placement, caption, alt_text AS AltText FROM figure WHERE id = @id", new { id = _figure });
    }

    private async Task<int> FiguresInSceneAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM figure WHERE scene_id = @Scene", new { Scene });
    }

    private async Task<(Guid? Asset, string? Caption)> OpenerAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(Guid?, string?)>(
            "SELECT opener_asset_id, opener_caption FROM act WHERE id = @id", new { id = SeedIds.Acts.PlayersGuideAct1 });
    }

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>("SELECT body, version FROM prose WHERE scene_id = @Scene", new { Scene });
    }
}
