using System.Text.RegularExpressions;
using Agoda.IoC.Core;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Render;

// Loads what a render needs in one connection (ManuscriptSnapshot). Live rows only: archived acts, chapters, scenes,
// figures, entries and uses are left out, so their directives and links are reported as missing. It does not check
// who is asking: the endpoint or job that calls the renderer has already checked the book.
public interface IManuscriptReader
{
    // 404 unknown book, or a scene or chapter that is not a live one of that book.
    Task<ManuscriptSnapshot> ReadAsync(Guid bookId, RenderScope scope, Guid? scopeId, string imagePurpose, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed partial class ManuscriptReader(NpgsqlDataSource dataSource) : IManuscriptReader
{
    [GeneratedRegex("^::(?:spell|statblock)\\{kb=\"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\"\\}$", RegexOptions.Multiline)]
    private static partial Regex EntryDirective();

    public async Task<ManuscriptSnapshot> ReadAsync(Guid bookId, RenderScope scope, Guid? scopeId, string imagePurpose, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var book = await connection.QuerySingleOrDefaultAsync<RenderBook>(new CommandDefinition(
            """
            SELECT id, title, subtitle, author_name AS AuthorName
            FROM book WHERE id = @bookId AND kind = 'book' AND archived_at IS NULL
            """,
            new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Book {bookId} was not found.");

        var acts = (await connection.QueryAsync<RenderAct>(new CommandDefinition(
            """
            SELECT id, title, opener_asset_id AS OpenerAssetId, opener_caption AS OpenerCaption
            FROM act WHERE book_id = @bookId AND archived_at IS NULL ORDER BY position, id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();

        var chapterRows = (await connection.QueryAsync<ChapterRow>(new CommandDefinition(
            """
            SELECT c.id, c.act_id AS ActId, c.title, c.is_numbered AS IsNumbered,
                   c.opener_asset_id AS OpenerAssetId, c.opener_caption AS OpenerCaption
            FROM chapter c JOIN act a ON a.id = c.act_id
            WHERE c.book_id = @bookId AND c.archived_at IS NULL AND a.archived_at IS NULL
            ORDER BY a.position, a.id, c.position, c.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();

        var sceneRows = (await connection.QueryAsync<SceneRow>(new CommandDefinition(
            """
            SELECT s.id, s.chapter_id AS ChapterId, coalesce(p.body, '') AS Body
            FROM scene s LEFT JOIN prose p ON p.scene_id = s.id
            WHERE s.book_id = @bookId AND s.archived_at IS NULL
            ORDER BY s.position, s.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();

        var inScope = ChaptersInScope(bookId, scope, scopeId, chapterRows, sceneRows);
        var number = 0;
        var chapters = chapterRows.Select(c => new RenderChapter(
            c.Id, c.ActId, c.Title, c.IsNumbered, c.IsNumbered ? ++number : null, c.OpenerAssetId, c.OpenerCaption, inScope.Contains(c.Id))).ToList();
        var chapterOrder = chapters.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var scenes = sceneRows
            .Where(s => scope == RenderScope.Scene ? s.Id == scopeId : inScope.Contains(s.ChapterId))
            .OrderBy(s => chapterOrder.GetValueOrDefault(s.ChapterId))
            .Select(s => new RenderScene(s.Id, s.ChapterId, s.Body))
            .ToList();
        var sceneIds = scenes.Select(s => s.Id).ToArray();

        var figures = (await connection.QueryAsync<RenderFigure>(new CommandDefinition(
            """
            SELECT id, asset_id AS AssetId, placement, caption, alt_text AS AltText
            FROM figure WHERE scene_id = ANY(@sceneIds) AND archived_at IS NULL
            """,
            new { sceneIds }, cancellationToken: cancellationToken))).ToDictionary(f => f.Id);

        var openers = scope == RenderScope.Scene
            ? []
            : acts.Where(a => chapters.Any(c => c.InScope && c.ActId == a.Id)).Select(a => a.OpenerAssetId)
                .Concat(chapters.Where(c => c.InScope).Select(c => c.OpenerAssetId));
        var assetIds = figures.Values.Select(f => f.AssetId).Concat(openers.OfType<Guid>()).Distinct().ToArray();
        var images = (await connection.QueryAsync<RenderImage>(new CommandDefinition(
            """
            SELECT r.asset_id AS AssetId, r.purpose, r.mime, r.width_px AS WidthPx, r.height_px AS HeightPx, r.storage_key AS StorageKey
            FROM asset_rendition r JOIN asset a ON a.id = r.asset_id
            WHERE r.asset_id = ANY(@assetIds) AND r.purpose = @imagePurpose AND a.archived_at IS NULL
            """,
            new { assetIds, imagePurpose }, cancellationToken: cancellationToken))).ToDictionary(i => i.AssetId);

        var entryIds = scenes.SelectMany(s => EntryDirective().Matches(s.Body).Select(m => Guid.Parse(m.Groups[1].Value))).Distinct().ToArray();
        var entries = (await connection.QueryAsync<RenderEntry>(new CommandDefinition(
            """
            SELECT id, name, fields::text AS Fields, body
            FROM kb_entry WHERE id = ANY(@entryIds) AND archived_at IS NULL
            """,
            new { entryIds }, cancellationToken: cancellationToken))).ToDictionary(e => e.Id);

        var citations = (await connection.QueryAsync<RenderCitation>(new CommandDefinition(
            """
            SELECT u.id AS UseId, r.authors::text AS Authors, r.title, r.year, r.publisher, u.locator
            FROM reference_use u JOIN reference r ON r.id = u.reference_id
            WHERE u.scene_id = ANY(@sceneIds) AND u.archived_at IS NULL
            """,
            new { sceneIds }, cancellationToken: cancellationToken))).ToDictionary(c => c.UseId);

        return new ManuscriptSnapshot(book, scope, acts, chapters, scenes, figures, images, entries, citations);
    }

    private static HashSet<Guid> ChaptersInScope(Guid bookId, RenderScope scope, Guid? scopeId, List<ChapterRow> chapters, List<SceneRow> scenes)
    {
        switch (scope)
        {
            case RenderScope.Book:
                return chapters.Select(c => c.Id).ToHashSet();
            case RenderScope.Chapter when chapters.Any(c => c.Id == scopeId):
                return [scopeId!.Value];
            case RenderScope.Scene when scenes.FirstOrDefault(s => s.Id == scopeId) is { } scene && chapters.Any(c => c.Id == scene.ChapterId):
                return [scene.ChapterId];
            default:
                throw ApiException.NotFound($"The {scope.ToString().ToLowerInvariant()} {scopeId} is not a live part of book {bookId}.");
        }
    }

    private sealed record ChapterRow(Guid Id, Guid ActId, string Title, bool IsNumbered, Guid? OpenerAssetId, string? OpenerCaption);

    private sealed record SceneRow(Guid Id, Guid ChapterId, string Body);
}
