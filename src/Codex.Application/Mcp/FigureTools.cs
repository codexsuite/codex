using System.ComponentModel;
using System.Text.Json;
using Codex.Application.Features.Print;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// F. Art (#65): place_figure, update_figure and set_opener, adapters over the Print lane's FigureCommands and
// OpenerCommands. A figure is a row plus its ::figure line, written together; the agent never types the line itself
// (McpProseRules). Images come from upload_image / create_image_upload (ImageTools).
[McpServerToolType]
public sealed class FigureTools(
    McpScenes scenes, McpRefs refs, McpQueries queries, IProseStore prose, FigureCommands figures, OpenerCommands openers, McpChangeSets changeSets,
    IBookAccess access)
{
    public sealed record FigureResult(McpItem Figure, Guid Asset, string Placement, string? Caption, string? AltText, bool Removed = false)
        : McpWriteResult;

    public sealed record OpenerResult(Guid? Asset, string? Caption) : McpWriteResult;

    [McpServerTool(Name = "place_figure", Title = "Place an image in a scene", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Places an uploaded image (upload_image, create_image_upload or list_images) as a figure in a scene: writes the figure and its " +
                 "::figure line together. `at` is \"start\", \"end\", {\"after\": {\"text\": …}} or {\"before\": {\"text\": …}}, as for " +
                 "upsert_beat. placement is column (default), page, spread or float-top. Needs expected_version; a stale one fails with conflict " +
                 "and writes nothing. undo_id takes the line out, which archives the figure.")]
    public async Task<FigureResult> PlaceFigureAsync(
        McpServer server,
        [Description("The scene: an id, or \"5.2\" with `book`.")] string scene,
        [Description("Where the ::figure line goes.")] JsonElement at,
        [Description("The image's asset id.")] string asset,
        [Description("The scene's version from read_manuscript.")] int expected_version,
        [Description("column, page, spread or float-top.")] string placement = "column",
        [Description("The caption printed with the figure.")] string? caption = null,
        [Description("Alt text for screen readers and the ebook.")] string? alt_text = null,
        [Description("The book, when `scene` is a display number.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var assetId = AssetId(asset);
        var place = ParsePlacement(placement)!;
        var target = await scenes.SceneAsync(server, book, scene, cancellationToken);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
        var sceneId = target.Scene.Dto.Id;
        var current = await prose.GetAsync(sceneId, cancellationToken);
        if (current.Version != expected_version)
        {
            throw ProseTools.Conflict(target, expected_version, current.Version);
        }

        var figureId = Guid.CreateVersion7();
        var body = ProseTools.InsertLine(current.Body, at, $"::figure{{id=\"{figureId}\"}}");
        int version;
        try
        {
            version = (await figures.CreateAsync(sceneId,
                new CreateFigureRequest(figureId, assetId, body, current.Version, place, caption, alt_text), cancellationToken)).Version;
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
        {
            throw ProseTools.Conflict(target, expected_version, (await prose.GetAsync(sceneId, cancellationToken)).Version);
        }

        var caller = McpCaller.From(server);
        await changeSets.CutAgentRevisionAsync(sceneId, current.Body, caller, cancellationToken);
        await scenes.ReloadAsync(server, target, cancellationToken);
        var undoId = await changeSets.RecordAsync(target.Outline.Book.Id, "place_figure", $"Placed a figure in {target.Item.Label}", caller,
            [new McpUndoStep("prose", sceneId, Body: current.Body, Version: version)], cancellationToken);
        var row = (await queries.FigureAsync(figureId, cancellationToken))!;
        return new FigureResult(Item(target, row), row.AssetId, row.Placement, row.Caption, row.AltText)
        {
            Item = target.Item, Version = version, UndoId = undoId,
            SideEffects = [$"A ::figure line was added to {target.Item.Label}."],
            Affected = [new McpAffected("figure", figureId, "created", null, null)],
        };
    }

    [McpServerTool(Name = "update_figure", Title = "Change a figure", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Changes a figure's caption, alt_text, placement or image (`asset`); an empty caption or alt_text clears it. Or `remove` " +
                 "it (with expected_version): its ::figure line is taken out and the figure archived, as deleting the card in Write does. " +
                 "undo_id puts the old values, or the line, back.")]
    public async Task<FigureResult> UpdateFigureAsync(
        McpServer server,
        [Description("The figure's id (from place_figure, or a ::figure line in read_manuscript).")] Guid figure,
        [Description("New caption; \"\" clears it.")] string? caption = null,
        [Description("New alt text; \"\" clears it.")] string? alt_text = null,
        [Description("column, page, spread or float-top.")] string? placement = null,
        [Description("A different image's asset id.")] string? asset = null,
        [Description("Take the figure out of its scene (needs expected_version, and no other change).")] bool remove = false,
        [Description("Removing: the scene's version from read_manuscript.")] int? expected_version = null,
        CancellationToken cancellationToken = default)
    {
        var before = await queries.FigureAsync(figure, cancellationToken) ?? throw McpToolException.NotFound($"No live figure {figure}.");
        var target = await scenes.SceneAsync(server, null, before.SceneId.ToString(), cancellationToken);
        await access.RequireAsync(target.Outline.Book.Id, BookRole.Write, cancellationToken);
        var caller = McpCaller.From(server);
        if (remove)
        {
            return await RemoveAsync(server, target, before, caption != null || alt_text != null || placement != null || asset != null, expected_version, caller, cancellationToken);
        }
        if (caption == null && alt_text == null && placement == null && asset == null)
        {
            throw McpToolException.Invalid("caption", "Give caption, alt_text, placement or asset to change, or remove.");
        }

        await figures.UpdateAsync(figure, new PatchFigureRequest(asset == null ? null : AssetId(asset), ParsePlacement(placement), caption, alt_text), cancellationToken);
        var after = (await queries.FigureAsync(figure, cancellationToken))!;
        var undoId = await changeSets.RecordAsync(target.Outline.Book.Id, "update_figure", $"Changed a figure in {target.Item.Label}", caller,
            [new McpUndoStep("patch_figure", figure, Parent: before.AssetId, Status: before.Placement, Summary: before.Caption, Body: before.AltText,
                Now: new McpUndoState(Parent: after.AssetId, Status: after.Placement, Summary: after.Caption ?? "", Body: after.AltText ?? ""))],
            cancellationToken);
        return new FigureResult(Item(target, after), after.AssetId, after.Placement, after.Caption, after.AltText)
        {
            Item = target.Item, UndoId = undoId, Affected = Changes(before, after),
        };
    }

    [McpServerTool(Name = "set_opener", Title = "Set an act or chapter opener", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Sets the full-page opener art of an act (a part opener, as LaTeX \\part with its art) or a chapter: `asset` and an optional " +
                 "`caption`. `clear` removes it. undo_id puts the previous opener back.")]
    public async Task<OpenerResult> SetOpenerAsync(
        McpServer server,
        [Description("The act or chapter: an id, \"act:2\", \"5\" or \"ch:<slug>\".")] string target,
        [Description("The image's asset id.")] string? asset = null,
        [Description("The caption printed with the opener.")] string? caption = null,
        [Description("Remove the opener instead.")] bool clear = false,
        [Description("The book, when `target` is not an id (default: the most recently edited book).")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        if (clear == (asset != null))
        {
            throw McpToolException.Invalid("asset", clear ? "clear takes no asset." : "Give the opener's asset, or clear.");
        }
        Guid? assetId = clear ? null : AssetId(asset!);
        var bookRef = book;
        if (Guid.TryParse(target.Trim(), out var id))
        {
            bookRef = (await queries.BookOfActOrChapterAsync(id, cancellationToken)
                       ?? throw McpToolException.NotFound($"No act or chapter {id}.")).ToString();
        }
        var targetBook = await refs.BookAsync(bookRef, cancellationToken);
        await access.RequireAsync(targetBook.Id, BookRole.Write, cancellationToken);
        var outline = await refs.OutlineAsync(targetBook, McpRefs.SessionKey(server), cancellationToken);
        var item = McpRefs.Item(outline, target);
        var kind = item.Kind switch
        {
            "act" => BookItem.Act,
            "chapter" => BookItem.Chapter,
            _ => throw McpToolException.Invalid("target", $"{item.Label} is a {item.Kind}; an opener belongs to an act or a chapter."),
        };

        var before = await openers.GetAsync(kind, item.Id, cancellationToken);
        var request = new SetOpenerRequest(assetId, caption);
        var after = kind == BookItem.Act
            ? await openers.SetActAsync(item.Id, request, cancellationToken)
            : await openers.SetChapterAsync(item.Id, request, cancellationToken);
        var undoId = await changeSets.RecordAsync(outline.Book.Id, "set_opener",
            clear ? $"Cleared the opener of {item.Label}" : $"Set the opener of {item.Label}", McpCaller.From(server),
            [new McpUndoStep("set_opener", item.Id, Parent: before.AssetId, Summary: before.Caption, Title: after.Kind,
                Now: new McpUndoState(Body: after.AssetId?.ToString() ?? "", Summary: after.Caption ?? ""))],
            cancellationToken);
        return new OpenerResult(after.AssetId, after.Caption)
        {
            Item = item, UndoId = undoId,
            Affected = [new McpAffected(after.Kind, item.Id, "opener", before.AssetId?.ToString(), after.AssetId?.ToString())],
        };
    }

    private async Task<FigureResult> RemoveAsync(
        McpServer server, McpScenes.Target target, McpQueries.FigureRow before, bool otherChanges, int? expectedVersion, McpCaller caller,
        CancellationToken cancellationToken)
    {
        if (otherChanges)
        {
            throw McpToolException.Invalid("remove", "remove takes no other change.");
        }
        if (expectedVersion is not { } expected)
        {
            throw McpToolException.Invalid("expected_version", "Removing a figure changes the prose: pass the scene's expected_version.");
        }

        var sceneId = target.Scene.Dto.Id;
        var current = await prose.GetAsync(sceneId, cancellationToken);
        if (current.Version != expected)
        {
            throw ProseTools.Conflict(target, expected, current.Version);
        }
        int version;
        try
        {
            version = (await figures.RemoveAsync(before.Id, new RemoveFigureRequest(expected), cancellationToken)).Version;
        }
        catch (ApiException e) when (e.Code == ApiErrorCodes.Conflict)
        {
            throw ProseTools.Conflict(target, expected, (await prose.GetAsync(sceneId, cancellationToken)).Version);
        }

        await changeSets.CutAgentRevisionAsync(sceneId, current.Body, caller, cancellationToken);
        await scenes.ReloadAsync(server, target, cancellationToken);
        var undoId = await changeSets.RecordAsync(target.Outline.Book.Id, "update_figure", $"Removed a figure from {target.Item.Label}", caller,
            [new McpUndoStep("prose", sceneId, Body: current.Body, Version: version)], cancellationToken);
        return new FigureResult(Item(target, before), before.AssetId, before.Placement, before.Caption, before.AltText, Removed: true)
        {
            Item = target.Item, Version = version, UndoId = undoId,
            SideEffects = [$"The ::figure line was removed from {target.Item.Label}: the figure is archived, as deleting it in Write does."],
            Affected = [new McpAffected("figure", before.Id, "archived", "false", "true")],
        };
    }

    private static McpItem Item(McpScenes.Target target, McpQueries.FigureRow figure) =>
        new(figure.Id, "figure", $"{target.Item.Label} · figure “{figure.Caption ?? figure.AltText ?? "untitled"}”", target.Item.OpenUrl);

    private static List<McpAffected> Changes(McpQueries.FigureRow before, McpQueries.FigureRow after) =>
        new (string Field, string? From, string? To)[]
            {
                ("asset", before.AssetId.ToString(), after.AssetId.ToString()), ("placement", before.Placement, after.Placement),
                ("caption", before.Caption, after.Caption), ("alt_text", before.AltText, after.AltText),
            }
            .Where(c => c.From != c.To)
            .Select(c => new McpAffected("figure", after.Id, c.Field, c.From, c.To)).ToList();

    private static Guid AssetId(string asset) =>
        Guid.TryParse(asset.Trim(), out var id) ? id : throw McpToolException.Invalid("asset", "asset is an image id from upload_image or list_images.");

    private static string? ParsePlacement(string? placement)
    {
        if (placement == null)
        {
            return null;
        }
        var value = placement.Trim().ToLowerInvariant();
        return FigureCommands.Placements.Contains(value)
            ? value
            : throw McpToolException.Invalid("placement", "placement is column, page, spread or float-top.");
    }
}
