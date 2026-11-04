using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print;

// Reads one figure row for the Write card (the line in prose.body only carries its id).
public interface IFigureService
{
    // 404 unknown or archived figure. A figure whose asset is archived is returned with Missing set.
    Task<FigureDto> GetAsync(Guid figureId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class FigureService(NpgsqlDataSource dataSource, IRenderService renderer, IBookAccess access) : IFigureService
{
    public async Task<FigureDto> GetAsync(Guid figureId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Figure, figureId, BookRole.View, cancellationToken, $"Figure {figureId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<FigureRow>(new CommandDefinition(
            """
            SELECT f.id, f.scene_id AS SceneId, s.book_id AS BookId, f.asset_id AS AssetId, f.placement, f.caption, f.alt_text AS AltText,
                   a.archived_at IS NOT NULL AS AssetArchived, a.width_px AS WidthPx, a.height_px AS HeightPx,
                   EXISTS (SELECT 1 FROM asset_rendition r WHERE r.asset_id = a.id AND r.purpose = 'web') AS HasWeb
            FROM figure f
            JOIN scene s ON s.id = f.scene_id
            JOIN asset a ON a.id = f.asset_id
            WHERE f.id = @figureId AND f.archived_at IS NULL
            """,
            new { figureId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Figure {figureId} was not found.");

        if (row.AssetArchived)
        {
            return new FigureDto(row.Id, row.SceneId, row.AssetId, row.Placement, row.Caption, row.AltText, true, null, null, null, null);
        }

        AssetDpiDto? dpi = null;
        if (row.WidthPx is { } width && row.HeightPx is { } height)
        {
            var (template, trim, _) = await renderer.ResolveAsync(new RenderRequest(row.BookId, RenderScope.Book), cancellationToken);
            dpi = EffectiveDpi.Compute(width, height, trim, template.Page.Columns, row.Placement, EffectiveDpi.PrintTarget, template.Page);
        }

        return new FigureDto(
            row.Id, row.SceneId, row.AssetId, row.Placement, row.Caption, row.AltText, false,
            row.HasWeb ? $"/api/assets/{row.AssetId}/renditions/web" : null, row.WidthPx, row.HeightPx, dpi);
    }

    private sealed record FigureRow(
        Guid Id, Guid SceneId, Guid BookId, Guid AssetId, string Placement, string? Caption, string? AltText,
        bool AssetArchived, int? WidthPx, int? HeightPx, bool HasWeb);
}
