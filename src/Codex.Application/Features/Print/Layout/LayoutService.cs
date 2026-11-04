using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Layout;

// The Layout view's commands (L7.3): the templates to pick from, the book's choice, upgrading its pinned version, and
// Quick preview. Plain application commands so the MCP print tools can call them. Preview needs pandoc (503
// not_configured without it).
public interface ILayoutService
{
    // The built-in templates and the book owner's own (not archived), each at the version the book would render it with,
    // and what the book uses now (its stored choice, else the default template's first trim). 404 unknown book.
    Task<LayoutStateDto> GetAsync(Guid bookId, CancellationToken cancellationToken);

    // Makes the template (a built-in's or the owner's slug) the book's choice, keeping the version, trim and options the
    // book stored for it before; a template it never used is pinned at its newest published version. 404 unknown book or
    // template; 400 a trim the template does not offer or an option it does not declare or of the wrong type.
    Task<LayoutStateDto> SelectAsync(Guid bookId, LayoutSelectionRequest request, CancellationToken cancellationToken);

    // Re-pins the book's current template to another published version of it (the newest when the request names none),
    // carrying the trim and options over where the new version still offers them. Other books keep their pin.
    // 404 unknown book or version, or a book with no template chosen; 400 a version of another template.
    Task<LayoutStateDto> UpgradeAsync(Guid bookId, LayoutUpgradeRequest request, CancellationToken cancellationToken);

    // Pandoc html5 of the scope with the template's preview.css, the figures' effective dpi and the issues found.
    // 404 unknown book, scene, chapter or template version; 400 a bad scope, trim or option.
    Task<LayoutPreviewDto> PreviewAsync(Guid bookId, LayoutPreviewRequest request, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class LayoutService(
    NpgsqlDataSource dataSource,
    IRenderService renderer,
    IBookAccess access,
    TimeProvider clock) : ILayoutService
{
    public async Task<LayoutStateDto> GetAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await RequireBookAsync(bookId, BookRole.View, cancellationToken);
        var chosen = await renderer.ResolveLayoutAsync(new RenderRequest(bookId, RenderScope.Book), cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TemplateRow>(new CommandDefinition(
            """
            SELECT t.id AS TemplateId, t.description AS Description, t.owner_id IS NOT NULL AS IsUserTemplate,
                   coalesce(blt.template_version_id, latest.id) AS VersionId, latest.id AS LatestVersionId,
                   src.name || ' v' || sv.version AS ForkedFrom
            FROM layout_template t
            JOIN book b ON b.id = @bookId
            JOIN LATERAL (
                SELECT v.id FROM layout_template_version v
                WHERE v.template_id = t.id AND v.published_at IS NOT NULL
                ORDER BY v.major DESC, v.minor DESC, v.patch DESC LIMIT 1
            ) latest ON true
            LEFT JOIN book_layout_template blt ON blt.book_id = b.id AND blt.template_id = t.id
            LEFT JOIN layout_template_version sv ON sv.id = t.forked_from_version_id
            LEFT JOIN layout_template src ON src.id = sv.template_id
            -- Built-ins and the book owner's own templates, whoever is looking (#80): a shared book offers its owner's.
            WHERE (t.owner_id IS NULL OR t.owner_id = b.owner_id) AND (t.archived_at IS NULL OR t.id = @chosen)
            ORDER BY t.owner_id IS NOT NULL, CASE WHEN t.owner_id IS NULL THEN t.slug ELSE lower(t.name) END, t.id
            """,
            new { bookId, chosen = chosen.TemplateId }, cancellationToken: cancellationToken));

        var list = new List<LayoutTemplateDto>();
        foreach (var row in rows)
        {
            var layout = await renderer.ResolveLayoutAsync(new RenderRequest(bookId, RenderScope.Book, TemplateVersionId: row.VersionId), cancellationToken);
            var newer = row.LatestVersionId == row.VersionId ? null : await ChangesSinceAsync(connection, row.VersionId, cancellationToken);
            list.Add(Describe(layout.Template, layout.Options) with
            {
                TemplateId = row.TemplateId,
                VersionId = row.VersionId,
                IsUserTemplate = row.IsUserTemplate,
                Description = row.Description,
                ForkedFrom = row.ForkedFrom,
                LatestVersion = newer?.Version,
                LatestVersionId = newer is null ? null : row.LatestVersionId,
                LatestChangelog = newer?.Changelog,
            });
        }

        return new LayoutStateDto(chosen.Template.Slug, chosen.Trim, list);
    }

    public async Task<LayoutStateDto> SelectAsync(Guid bookId, LayoutSelectionRequest request, CancellationToken cancellationToken)
    {
        await RequireBookAsync(bookId, BookRole.Write, cancellationToken);
        var layout = await renderer.ResolveLayoutAsync(
            new RenderRequest(bookId, RenderScope.Book, Template: request.Template, Trim: request.Trim, Options: request.Options), cancellationToken);
        await PinAsync(bookId, layout, cancellationToken);
        return await GetAsync(bookId, cancellationToken);
    }

    public async Task<LayoutStateDto> UpgradeAsync(Guid bookId, LayoutUpgradeRequest request, CancellationToken cancellationToken)
    {
        await RequireBookAsync(bookId, BookRole.Write, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<(Guid TemplateId, Guid LatestId)?>(new CommandDefinition(
            """
            SELECT c.template_id AS TemplateId, c.latest_version_id AS LatestId
            FROM book_layout_current c WHERE c.book_id = @bookId
            """,
            new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("This book has no template chosen yet; pick one first.");
        var target = request.TemplateVersionId ?? current.LatestId;
        var belongs = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM layout_template_version WHERE id = @target AND template_id = @templateId AND published_at IS NOT NULL)",
            new { target, current.TemplateId }, cancellationToken: cancellationToken));
        if (!belongs)
        {
            throw ApiException.Validation("That version is not a published version of the book's template.");
        }

        // The resolve keeps the stored trim and options where the target version still offers them, and drops the rest.
        var layout = await renderer.ResolveLayoutAsync(new RenderRequest(bookId, RenderScope.Book, TemplateVersionId: target), cancellationToken);
        await PinAsync(bookId, layout, cancellationToken, repin: true);
        return await GetAsync(bookId, cancellationToken);
    }

    // Stores the choice. A book that already pinned a version of this template keeps it unless repin; the trim and
    // options are the book's to change.
    private async Task PinAsync(Guid bookId, ResolvedLayout layout, CancellationToken cancellationToken, bool repin = false)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book_layout_template (book_id, template_id, template_version_id, trim_size, options)
            VALUES (@bookId, @templateId, @versionId, @trim, @options::jsonb)
            ON CONFLICT (book_id, template_id) DO UPDATE SET
                template_version_id = CASE WHEN @repin THEN EXCLUDED.template_version_id ELSE book_layout_template.template_version_id END,
                trim_size = EXCLUDED.trim_size, options = EXCLUDED.options, updated_at = now()
            """,
            new { bookId, templateId = layout.TemplateId, versionId = layout.TemplateVersionId, trim = layout.Trim, options = JsonSerializer.Serialize(layout.Options), repin },
            transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO book_layout (book_id, template_id) VALUES (@bookId, @templateId)
            ON CONFLICT (book_id) DO UPDATE SET template_id = EXCLUDED.template_id, updated_at = now()
            """,
            new { bookId, templateId = layout.TemplateId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    // The newest published version after this one, and every changelog line since this one, newest version first.
    private static async Task<(string Version, IReadOnlyList<string> Changelog)?> ChangesSinceAsync(
        NpgsqlConnection connection, Guid versionId, CancellationToken cancellationToken)
    {
        var newer = (await connection.QueryAsync<(string Version, string Changelog)>(new CommandDefinition(
            """
            SELECT n.version AS Version, to_jsonb(n.changelog)::text AS Changelog
            FROM layout_template_version v
            JOIN layout_template_version n ON n.template_id = v.template_id AND n.published_at IS NOT NULL
                 AND (n.major, n.minor, n.patch) > (v.major, v.minor, v.patch)
            WHERE v.id = @versionId
            ORDER BY n.major DESC, n.minor DESC, n.patch DESC
            """,
            new { versionId }, cancellationToken: cancellationToken))).ToList();
        return newer.Count == 0
            ? null
            : (newer[0].Version, newer.SelectMany(n => JsonSerializer.Deserialize<string[]>(n.Changelog) ?? []).ToList());
    }

    public async Task<LayoutPreviewDto> PreviewAsync(Guid bookId, LayoutPreviewRequest request, CancellationToken cancellationToken)
    {
        await RequireBookAsync(bookId, BookRole.View, cancellationToken);
        var scope = request.Scope switch
        {
            "scene" => RenderScope.Scene,
            "chapter" => RenderScope.Chapter,
            "book" => RenderScope.Book,
            _ => throw ApiException.Validation("The scope must be scene, chapter or book."),
        };
        if (scope != RenderScope.Book && request.ScopeId is null)
        {
            throw ApiException.Validation($"Name the {request.Scope} to preview.");
        }

        var render = new RenderRequest(bookId, scope, request.ScopeId, request.Template, request.Trim, request.Options, "web", request.TemplateVersionId);
        var html = await renderer.RenderHtmlAsync(render, cancellationToken);
        var template = html.Template;
        var figures = await FiguresAsync(bookId, template, html.Document.Trim, cancellationToken);
        var flags = html.Document.Flags.Select(f => new LayoutFlagDto(f.Kind, f.SceneId, f.Detail, f.Kept)).ToList();
        return new LayoutPreviewDto(
            html.Html, html.PreviewCss, template.Slug, template.Name, template.Version, html.Document.Trim, html.Document.Options,
            template.HasPreviewCss ? [] : [TemplatePackage.NoPreviewCssNotice], flags, figures, html.Document.ContentHash, clock.GetUtcNow(),
            html.Document.TemplateVersionId);
    }

    private async Task<IReadOnlyList<LayoutFigureDto>> FiguresAsync(Guid bookId, TemplatePackage template, string trim, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<FigureRow>(new CommandDefinition(
            """
            SELECT f.id AS FigureId, f.asset_id AS AssetId, f.scene_id AS SceneId, a.filename AS Filename, f.caption AS Caption,
                   f.placement AS Placement, a.width_px AS WidthPx, a.height_px AS HeightPx
            FROM figure f
            JOIN scene s ON s.id = f.scene_id
            JOIN asset a ON a.id = f.asset_id
            WHERE s.book_id = @bookId AND f.archived_at IS NULL AND s.archived_at IS NULL
            ORDER BY f.created_at, f.id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return rows.Select(r =>
        {
            if (r.WidthPx is not { } width || r.HeightPx is not { } height)
            {
                return new LayoutFigureDto(r.FigureId, r.AssetId, r.SceneId, r.Filename, r.Caption, r.Placement, r.WidthPx, r.HeightPx, null, null, null, "ok");
            }

            var dpi = EffectiveDpi.Compute(width, height, trim, template.Page.Columns, r.Placement, EffectiveDpi.PrintTarget, template.Page);
            return new LayoutFigureDto(
                r.FigureId, r.AssetId, r.SceneId, r.Filename, r.Caption, r.Placement, width, height,
                dpi.PlacedWidthIn, dpi.PlacedHeightIn, dpi.EffectiveDpi, dpi.Status);
        }).ToList();
    }

    private static LayoutTemplateDto Describe(TemplatePackage package, IReadOnlyDictionary<string, JsonElement> values) =>
        new(
            package.Slug, package.Name, package.Version, package.Engine, package.Trims, package.Page.Columns, package.HasPreviewCss,
            package.Options.Select(o => new LayoutOptionDto(
                o.Key, o.Value.Label ?? o.Key, o.Value.Type, o.Value.Choices ?? [], o.Value.Default.Clone(), values[o.Key].Clone())).ToList(),
            package.Notices);

    // The access rule first (a book the user cannot open is not_found, forbidden below minimum), then that it is a book
    // rather than a series.
    private async Task RequireBookAsync(Guid bookId, BookRole minimum, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, minimum, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }
    }

    private sealed record TemplateRow(Guid TemplateId, string? Description, bool IsUserTemplate, Guid VersionId, Guid LatestVersionId, string? ForkedFrom);

    private sealed record FigureRow(Guid FigureId, Guid AssetId, Guid SceneId, string Filename, string? Caption, string Placement, int? WidthPx, int? HeightPx);
}
