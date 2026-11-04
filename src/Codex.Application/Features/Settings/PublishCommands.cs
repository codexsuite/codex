using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Settings;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// Publish lifecycle (J26): marking a book Published saves a snapshot, which is a source export job (the book's .zip as it
// was) flagged settings.snapshot, so no table of its own. Published does not lock editing, and risks in References do not
// block it (they block Export for printer). A book with no chapters can't be published. Moving the status back is the
// ordinary settings PATCH. Anyone who can open the book reads the publish state; publishing is the owner's (#80).
[RegisterSingleton]
public sealed class PublishCommands(
    NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, IBookEvents events, TimeProvider clock)
{
    private const string SnapshotLabel = "Published";

    private sealed record BookFacts(string Status, int WordCount, int ChapterCount, int AtRiskCount, bool ExportRunning);

    private sealed record SnapshotRow(Guid Id, string Label, DateTimeOffset CreatedAt, int Words, string State, bool Waiting, bool HasFile);

    public async Task<PublishStateDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, bookId, cancellationToken);
    }

    public async Task<PublishStateDto> PublishAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Owner, cancellationToken, "That book doesn't exist.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // Lock the book so two publishes (two tabs) take turns.
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT 1 FROM book WHERE id = @bookId FOR NO KEY UPDATE", new { bookId }, transaction, cancellationToken: cancellationToken));
            var facts = await FactsAsync(connection, bookId, transaction, cancellationToken);
            if (facts.ChapterCount == 0)
            {
                throw ApiException.Blocked("Nothing to publish. Add a chapter first.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO export_job (book_id, kind, settings, status, step, progress, created_by, created_at)
                VALUES (@bookId, 'source', jsonb_build_object('snapshot', true, 'label', @label::text, 'words', @words::int),
                        'queued', 'Queued', 0, @user, @now)
                """,
                new { bookId, label = SnapshotLabel, words = facts.WordCount, user = currentUser.Id, now = clock.GetUtcNow().UtcDateTime },
                transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE book SET status = 'published' WHERE id = @bookId", new { bookId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new BookMetaChanged(bookId, ["status"]));
        return await ReadAsync(connection, bookId, cancellationToken);
    }

    private async Task<PublishStateDto> ReadAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken)
    {
        var facts = await FactsAsync(connection, bookId, null, cancellationToken);
        var rows = await connection.QueryAsync<SnapshotRow>(new CommandDefinition(
            """
            SELECT j.id, coalesce(j.settings ->> 'label', 'Published') AS Label, j.created_at AS CreatedAt,
                   coalesce((j.settings ->> 'words')::int, 0) AS Words, j.status AS State,
                   (j.status = 'queued' AND EXISTS (SELECT 1 FROM export_job o WHERE o.kind = 'source' AND o.status = 'running'
                                                    AND o.id <> j.id)) AS Waiting,
                   (j.output_asset_id IS NOT NULL) AS HasFile
            FROM export_job j
            WHERE j.book_id = @bookId AND j.kind = 'source' AND j.settings ->> 'snapshot' = 'true'
            ORDER BY j.created_at DESC, j.id DESC
            """,
            new { bookId }, cancellationToken: cancellationToken));
        var now = clock.GetUtcNow();
        var snapshots = rows.Select(r => new SnapshotDto(
            r.Id, r.Label, DisplayDate.ShortDay(r.CreatedAt, now, clock.LocalTimeZone), r.Words, r.State, r.Waiting,
            r.State == "succeeded" && r.HasFile ? $"/api/exports/{r.Id}/file" : null)).ToList();
        return new PublishStateDto(facts.Status, facts.WordCount, facts.ChapterCount, facts.AtRiskCount, facts.ExportRunning, snapshots);
    }

    private static async Task<BookFacts> FactsAsync(
        NpgsqlConnection connection, Guid bookId, NpgsqlTransaction? transaction, CancellationToken cancellationToken) =>
        await connection.QuerySingleAsync<BookFacts>(new CommandDefinition(
            """
            SELECT b.status AS Status,
                   coalesce((SELECT sum(ss.word_count) FROM scene_stats ss WHERE ss.book_id = b.id), 0)::int AS WordCount,
                   (SELECT count(*) FROM chapter c WHERE c.book_id = b.id AND c.archived_at IS NULL)::int AS ChapterCount,
                   coalesce((SELECT s.risk_count FROM book_reference_summary s WHERE s.book_id = b.id), 0)::int AS AtRiskCount,
                   EXISTS (SELECT 1 FROM export_job j WHERE j.book_id = b.id AND j.kind = 'source'
                           AND j.status IN ('queued', 'running')) AS ExportRunning
            FROM book b WHERE b.id = @bookId AND b.kind = 'book'
            """,
            new { bookId }, transaction, cancellationToken: cancellationToken));
}
