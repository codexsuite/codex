using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Export;

// Preflight (issue #13): what stands between the book as it is now and a good print, worked out live from the render
// core's flags, the figures' effective dpi for the template, trim and quality, and the copyright clearance. Blocks are
// copyright risks (References) and images whose file is missing; a final export is refused while any exist, a draft is not.
// Warnings: images below the target dpi, unknown directives, raw LaTeX the template skips, entries and references that
// no longer exist. Notes: raw LaTeX the template keeps. Overfull boxes only exist after typesetting, so the worker
// records them on the finished export instead.
public interface IExportPreflight
{
    Task<ExportPlan> PlanAsync(Guid bookId, ExportRequest request, CancellationToken cancellationToken);
}

// A validated request resolved against the book: the settings, the template version it would pin and its preflight.
public sealed record ExportPlan(ExportSettings Settings, Guid TemplateVersionId, AssembledDocument Document, PreflightDto Preflight);

[RegisterSingleton]
public sealed class ExportPreflight(NpgsqlDataSource dataSource, IRenderService renderer, ITemplateStore templates) : IExportPreflight
{
    private const int OverheadMegabytes = 1;

    public async Task<ExportPlan> PlanAsync(Guid bookId, ExportRequest request, CancellationToken cancellationToken)
    {
        var settings = ExportSettings.From(request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book' AND archived_at IS NULL)", new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }

        var document = await renderer.AssembleAsync(
            new RenderRequest(bookId, RenderScope.Book, null, request.Template, request.Trim, request.Options, settings.ImagePurpose), RenderTarget.Pdf, cancellationToken);
        // The version the book pinned (or the request's template at its pinned or newest version): what the job will render.
        var versionId = document.TemplateVersionId;
        var template = await templates.OpenAsync(versionId, cancellationToken);

        var blocks = new List<PreflightItemDto>();
        var warnings = new List<PreflightItemDto>();
        var notes = new List<PreflightItemDto>();

        var risks = await connection.QueryAsync<(Guid Id, string Title, string Reason)>(new CommandDefinition(
            """
            SELECT c.reference_id AS Id, r.title AS Title, c.reason AS Reason
            FROM reference_clearance c JOIN reference r ON r.id = c.reference_id
            WHERE c.book_id = @bookId AND c.status = 'risk'
            ORDER BY c.number NULLS LAST, r.title
            """,
            new { bookId }, cancellationToken: cancellationToken));
        blocks.AddRange(risks.Select(r => new PreflightItemDto(
            "block", "copyright_risk", $"{(string.IsNullOrWhiteSpace(r.Title) ? "An untitled source" : r.Title)}: copyright risk ({r.Reason.Replace('_', ' ')})", null, r.Id)));

        foreach (var flag in document.Flags)
        {
            var (severity, text) = flag.Kind switch
            {
                RenderFlag.MissingAsset => ("block", $"Image missing: {flag.Detail}"),
                RenderFlag.UnknownDirective => ("warn", $"Unknown directive: {flag.Detail}"),
                RenderFlag.RawLatex when flag.Kept => ("note", $"Raw LaTeX kept by this template: {flag.Detail}"),
                RenderFlag.RawLatex => ("warn", $"Raw LaTeX skipped by this template: {flag.Detail}"),
                RenderFlag.HiddenByComment => ("warn", flag.Detail),
                RenderFlag.MissingEntry => ("warn", $"Knowledge base entry missing: {flag.Detail}"),
                RenderFlag.MissingReference => ("warn", $"Reference missing: {flag.Detail}"),
                _ => ("warn", $"{flag.Kind}: {flag.Detail}"),
            };
            var dto = new PreflightItemDto(severity, flag.Kind, text, flag.SceneId, null);
            (severity switch { "block" => blocks, "warn" => warnings, _ => notes }).Add(dto);
        }

        var figures = await connection.QueryAsync<FigureRow>(new CommandDefinition(
            """
            SELECT f.scene_id AS SceneId, f.placement AS Placement, a.filename AS Filename, a.width_px AS WidthPx, a.height_px AS HeightPx
            FROM figure f
            JOIN scene s ON s.id = f.scene_id
            JOIN chapter c ON c.id = s.chapter_id
            JOIN act ac ON ac.id = c.act_id
            JOIN asset a ON a.id = f.asset_id
            WHERE s.book_id = @bookId AND f.archived_at IS NULL AND s.archived_at IS NULL AND c.archived_at IS NULL AND ac.archived_at IS NULL
            ORDER BY ac.position, c.position, s.position, f.created_at, f.id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        foreach (var figure in figures.Where(f => f.WidthPx is not null && f.HeightPx is not null))
        {
            var dpi = EffectiveDpi.Compute(
                figure.WidthPx!.Value, figure.HeightPx!.Value, document.Trim, template.Page.Columns, figure.Placement, settings.TargetDpi, template.Page);
            if (dpi.Status == "low")
            {
                warnings.Add(new PreflightItemDto(
                    "warn", "low_dpi", $"Low resolution: {figure.Filename} is {dpi.EffectiveDpi:0} dpi at its placed size, below {settings.TargetDpi}", figure.SceneId, null));
            }
        }

        var imageBytes = document.Images.Count == 0
            ? 0
            : await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT coalesce(sum(bytes), 0)::bigint FROM asset_rendition WHERE storage_key = ANY(@keys)",
                new { keys = document.Images.Select(i => i.Rendition.StorageKey).Distinct().ToArray() }, cancellationToken: cancellationToken));
        var megabytes = (int)Math.Ceiling(imageBytes / 1_048_576.0) + OverheadMegabytes;

        var preflight = new PreflightDto(
            blocks.Count > 0 ? "blocked" : "ready", blocks, warnings, notes, template.Slug, template.Version, document.Trim, megabytes);
        return new ExportPlan(settings, versionId, document, preflight);
    }

    private sealed record FigureRow(Guid SceneId, string Placement, string Filename, int? WidthPx, int? HeightPx);
}
