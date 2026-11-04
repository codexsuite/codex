using Codex.Contracts.Events;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Features.References;

// The manuscript scan "on every save": each ProseSaved is read back and its unmarked quotes become open quote_flag
// rows, and the scene's open flags whose quote is now cited or gone are closed. A quote already flagged or dismissed
// in the book shares its fingerprint, so it never comes back; a cited one reopens when its marker goes.
// A failed scan is logged and the next save tries again; it never reaches the host.
public sealed class QuoteFlagScanner(IBookEvents events, NpgsqlDataSource dataSource, ILogger<QuoteFlagScanner> logger) : IHostedService
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = events.Subscribe(null, HandleAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    private async Task HandleAsync(BookEvent bookEvent)
    {
        if (bookEvent is not ProseSaved saved)
        {
            return;
        }

        try
        {
            await ScanAsync(saved.BookId, saved.SceneId);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            logger.LogError(exception, "Quote scan failed for scene {SceneId}", saved.SceneId);
        }
    }

    public async Task ScanAsync(Guid bookId, Guid sceneId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var body = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT body FROM prose WHERE scene_id = @sceneId", new { sceneId }));
        var excerpts = QuoteFinder.Find(body ?? string.Empty).ToArray();
        await using var transaction = await connection.BeginTransactionAsync();

        // Open flags of this scene whose quote is no longer unmarked: a live use of the scene covering the same
        // words makes the flag cited; otherwise the quote is gone or carries a bare marker, and the flag goes so
        // the words can be raised again if they come back unmarked. "Not a quote" (dismissed) is never touched.
        // The fingerprint expression is quote_flag.fingerprint's.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            WITH stale AS (
                SELECT f.id, f.fingerprint FROM quote_flag f
                WHERE f.scene_id = @sceneId AND f.book_id = @bookId AND f.status = 'open' AND f.reason = 'quotation_marks'
                  AND f.fingerprint <> ALL (
                      SELECT md5(lower(btrim(regexp_replace(e, '[^[:alnum:]]+', ' ', 'g')))) FROM unnest(@excerpts) AS e)
            ), cited AS (
                UPDATE quote_flag f SET status = 'cited', resolved_use_id = u.id, resolved_at = now()
                FROM stale s
                CROSS JOIN LATERAL (
                    SELECT u.id FROM reference_use u
                    WHERE u.scene_id = @sceneId AND u.archived_at IS NULL
                      AND md5(lower(btrim(regexp_replace(u.excerpt, '[^[:alnum:]]+', ' ', 'g')))) = s.fingerprint
                    ORDER BY u.created_at DESC LIMIT 1
                ) u
                WHERE f.id = s.id
                RETURNING f.id
            )
            DELETE FROM quote_flag f USING stale s
            WHERE f.id = s.id AND f.id NOT IN (SELECT id FROM cited)
            """,
            new { excerpts, sceneId, bookId }, transaction));

        // New unmarked quotes become open flags; a cited flag whose words are unmarked again reopens here.
        // QuoteFinder dedupes on the plain text, but the unique key is the fingerprint, which ignores case and
        // punctuation: DISTINCT ON keeps one excerpt per fingerprint, since ON CONFLICT DO UPDATE refuses to touch
        // the same row twice in one command.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO quote_flag (book_id, scene_id, excerpt, reason)
            SELECT s.book_id, s.id, e.excerpt, 'quotation_marks'
            FROM (
                SELECT DISTINCT ON (md5(lower(btrim(regexp_replace(x.excerpt, '[^[:alnum:]]+', ' ', 'g')))))
                       x.excerpt
                FROM unnest(@excerpts) WITH ORDINALITY AS x (excerpt, ord)
                ORDER BY md5(lower(btrim(regexp_replace(x.excerpt, '[^[:alnum:]]+', ' ', 'g')))), x.ord
            ) AS e
            JOIN scene s ON s.id = @sceneId AND s.book_id = @bookId AND s.archived_at IS NULL
            ON CONFLICT (book_id, fingerprint) DO UPDATE
                SET status = 'open', scene_id = EXCLUDED.scene_id, resolved_use_id = NULL, resolved_by = NULL, resolved_at = NULL
                WHERE quote_flag.status = 'cited'
            """,
            new { excerpts, sceneId, bookId }, transaction));
        await transaction.CommitAsync();
    }
}
