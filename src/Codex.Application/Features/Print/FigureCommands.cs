using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print;

// Adds, changes and removes figures (#65). A figure is a row plus its `::figure{id}` line in the scene's prose, written
// in one transaction the way BlockCommands inserts a beat: the row starts archived, and the prose write brings it back
// (prose_sync_embeds). Taking the line out archives it again, and putting the line back (undo, Restore) revives it.
[RegisterSingleton]
public sealed class FigureCommands(NpgsqlDataSource dataSource, IProseStore prose, IBookAccess access, ICurrentUser currentUser)
{
    private const string EditorOrigin = "editor";

    public static readonly IReadOnlyList<string> Placements = ["column", "page", "spread", "float-top"];

    private sealed record FigureRow(Guid SceneId, Guid BookId);

    // 404 unknown scene or image; 400 a body without the figure's line, one that drops another embed, an unknown
    // placement, or an image outside the book and its series; 409 a stale version or a figure id already used.
    public async Task<CreateFigureResult> CreateAsync(Guid sceneId, CreateFigureRequest request, CancellationToken cancellationToken)
    {
        var bookId = await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, "Scene not found.");
        var body = request.Body ?? throw ApiException.Validation("Prose body is required.");
        if (!ProseDocument.Parse(body).ListEmbeds().Any(e => e.Kind == ProseEmbedKind.Figure && e.Id == request.FigureId))
        {
            throw ApiException.Validation("The body has no ::figure line for the new figure.");
        }
        var placement = Placement(request.Placement) ?? "column";
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await PlaceableAsset.RequireAsync(connection, bookId!.Value, request.AssetId, currentUser.Id, cancellationToken);
        }

        var versions = await prose.UpdateManyAsync([(sceneId, request.Version, current => Conserved(current, body))], EditorOrigin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "SELECT EXISTS (SELECT 1 FROM figure WHERE id = @figureId)", new { figureId = request.FigureId }, transaction, cancellationToken: ct)))
                    {
                        throw ApiException.Conflict("A figure with that id already exists.");
                    }

                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO figure (id, scene_id, asset_id, placement, caption, alt_text, archived_at)
                        VALUES (@figureId, @sceneId, @assetId, @placement, @caption, @altText, now())
                        """,
                        new
                        {
                            figureId = request.FigureId, sceneId, assetId = request.AssetId, placement,
                            caption = Text(request.Caption), altText = Text(request.AltText),
                        },
                        transaction, cancellationToken: ct));
                },
            },
            cancellationToken);
        return new CreateFigureResult(request.FigureId, versions[0]);
    }

    // 404 unknown or archived figure, or an unknown image; 400 an unknown placement or an image outside the book and its series.
    public async Task UpdateAsync(Guid figureId, PatchFigureRequest request, CancellationToken cancellationToken)
    {
        var bookId = await access.RequireAsync(BookItem.Figure, figureId, BookRole.Write, cancellationToken, "Figure not found.");
        var placement = Placement(request.Placement);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (request.AssetId is { } assetId)
        {
            await PlaceableAsset.RequireAsync(connection, bookId!.Value, assetId, currentUser.Id, cancellationToken);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE figure SET asset_id = coalesce(@assetId, asset_id), placement = coalesce(@placement, placement),
                              caption = CASE WHEN @caption::text IS NULL THEN caption ELSE nullif(@caption, '') END,
                              alt_text = CASE WHEN @altText::text IS NULL THEN alt_text ELSE nullif(@altText, '') END,
                              updated_at = now()
            WHERE id = @figureId AND archived_at IS NULL
            """,
            new { figureId, assetId = request.AssetId, placement, caption = request.Caption?.Trim(), altText = request.AltText?.Trim() },
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            throw ApiException.NotFound("Figure not found.");
        }
    }

    // Takes the figure's line out of its scene, which archives the figure. 404 unknown figure, or one whose line is not
    // in the scene (already removed); 409 a stale version.
    public async Task<RemoveFigureResult> RemoveAsync(Guid figureId, RemoveFigureRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Figure, figureId, BookRole.Write, cancellationToken, "Figure not found.");
        FigureRow row;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            row = await connection.QuerySingleOrDefaultAsync<FigureRow>(new CommandDefinition(
                "SELECT f.scene_id AS SceneId, s.book_id AS BookId FROM figure f JOIN scene s ON s.id = f.scene_id WHERE f.id = @figureId AND f.archived_at IS NULL",
                new { figureId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Figure not found.");
        }

        var version = await prose.UpdateAsync(row.SceneId, body =>
        {
            try
            {
                return ProseDocument.Parse(body).StripFigure(figureId).Text;
            }
            catch (ProseEditException)
            {
                throw ApiException.NotFound("The figure's line is not in its scene.");
            }
        }, request.Version, EditorOrigin, cancellationToken);
        return new RemoveFigureResult(row.SceneId, version);
    }

    private static string? Placement(string? placement)
    {
        if (placement is null)
        {
            return null;
        }
        var value = placement.Trim().ToLowerInvariant();
        return Placements.Contains(value) ? value : throw ApiException.Validation("Placement is column, page, spread or float-top.");
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The new body may add the figure's line but must keep every embed the scene already had.
    private static string Conserved(string current, string next)
    {
        var kept = ProseDocument.Parse(next).ListEmbeds().Select(e => (e.Kind, e.Id)).ToHashSet();
        return ProseDocument.Parse(current).ListEmbeds().All(e => kept.Contains((e.Kind, e.Id)))
            ? next
            : throw ApiException.Validation("The change would drop an embedded block (a beat, figure or addition) from the scene.");
    }
}
