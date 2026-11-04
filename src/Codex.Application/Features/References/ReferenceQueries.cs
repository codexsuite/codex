using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

public interface IReferenceQueries
{
    Task<IReadOnlyList<MarkerDto>> GetMarkersAsync(Guid bookId, CancellationToken cancellationToken);

    Task<ClearanceSummaryDto> GetSummaryAsync(Guid bookId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class ReferenceQueries(NpgsqlDataSource dataSource, IBookAccess access) : IReferenceQueries
{
    public async Task<IReadOnlyList<MarkerDto>> GetMarkersAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await RequireBookAsync(connection, bookId, cancellationToken);
        var rows = await connection.QueryAsync<MarkerDto>(new CommandDefinition(
            """
            SELECT o.use_id AS UseId, c.number::integer AS Number, c.status AS Status, r.id AS SourceId,
                   r.title AS SourceTitle, c.reason AS Reason, o.kind AS Kind, c.quoted_words AS QuotedWords,
                   ps.quote_word_limit AS Limit, o.scene_id AS SceneId, o.chapter_number::integer AS ChapterNumber,
                   o.scene_number::integer AS SceneNumber, u.generation_id AS GenerationId
            FROM reference_use_outline o
            JOIN reference_use u ON u.id = o.use_id
            JOIN reference r ON r.id = o.reference_id
            JOIN reference_clearance c ON c.reference_id = o.reference_id
            JOIN book_publication_settings ps ON ps.book_id = o.book_id
            WHERE o.book_id = @bookId
            ORDER BY o.book_order
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<ClearanceSummaryDto> GetSummaryAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ClearanceSummaryDto>(new CommandDefinition(
            """
            SELECT cleared_count AS Cleared, source_count AS Total, risk_count AS Risks,
                   needs_check_count AS NeedsCheck, not_cited_count AS NotCited,
                   CASE WHEN risk_count > 0 THEN risk_count ELSE needs_check_count END AS Badge,
                   (SELECT count(*)::int FROM reference r
                    WHERE r.book_id = @bookId AND r.archived_at IS NULL AND EXISTS (
                        SELECT 1 FROM permission_request q
                        WHERE q.reference_id = r.id AND q.status = 'sent' AND q.remind_at <= now()
                          AND q.created_at = (SELECT max(q2.created_at) FROM permission_request q2 WHERE q2.reference_id = r.id AND q2.status <> 'withdrawn'))) AS RemindersDue
            FROM book_reference_summary WHERE book_id = @bookId
            """,
            new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Book {bookId} was not found.");
    }

    private static async Task RequireBookAsync(NpgsqlConnection connection, Guid bookId, CancellationToken ct)
    {
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: ct)))
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }
    }
}
