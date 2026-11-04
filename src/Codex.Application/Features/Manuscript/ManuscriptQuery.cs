using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

[RegisterSingleton]
public sealed class ManuscriptQuery(NpgsqlDataSource dataSource, IBookAccess access)
{
    private sealed record SceneProseRow(Guid SceneId, string Body, int Version);

    public async Task<ManuscriptDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound("Book not found.");
        }

        // Reading order: act, chapter, scene positions. Archived scenes, chapters and acts are not part of the manuscript.
        var rows = await connection.QueryAsync<SceneProseRow>(new CommandDefinition(
            """
            SELECT s.id AS SceneId, coalesce(p.body, '') AS Body, coalesce(p.version, 0)::int AS Version
            FROM scene s
            JOIN chapter c ON c.id = s.chapter_id
            JOIN act a ON a.id = c.act_id
            LEFT JOIN prose p ON p.scene_id = s.id
            WHERE s.book_id = @bookId AND s.archived_at IS NULL AND c.archived_at IS NULL AND a.archived_at IS NULL
            ORDER BY a.position, c.position, c.id, s.position, s.id
            """,
            new { bookId }, cancellationToken: cancellationToken));

        return new ManuscriptDto(bookId, rows.Select(r => new ManuscriptSceneDto(r.SceneId, r.Body, r.Version)).ToList());
    }
}
