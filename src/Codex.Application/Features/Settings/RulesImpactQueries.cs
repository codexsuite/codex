using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Settings;

// The live strip under the quote limit: each cited source's quoted words against a limit that isn't saved yet.
// The candidate limit is written inside a transaction that is never committed, so reference_clearance answers with
// its own rule. A read (#80): it needs view, since the candidate limit is rolled back.
[RegisterSingleton]
public sealed class RulesImpactQueries(NpgsqlDataSource dataSource, IBookAccess access)
{
    private sealed record SourceRow(
        Guid ReferenceId, string Title, int? Number, int QuotedWords, int LyricWords, int FigureCount,
        bool NeedsPermission, string Status);

    public async Task<RulesImpactDto> GetAsync(Guid bookId, int? limit, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "That book doesn't exist.");
        if (limit is <= 0)
        {
            throw ApiException.Validation("The quote limit must be more than 0 words.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')",
            new { bookId }, transaction, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound("That book doesn't exist.");
        }
        if (limit != null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO publication_settings (book_id, quote_word_limit) VALUES (@bookId, @limit)
                ON CONFLICT (book_id) DO UPDATE SET quote_word_limit = EXCLUDED.quote_word_limit
                """,
                new { bookId, limit }, transaction, cancellationToken: cancellationToken));
        }
        var effective = await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT quote_word_limit FROM book_publication_settings WHERE book_id = @bookId",
            new { bookId }, transaction, cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<SourceRow>(new CommandDefinition(
            """
            SELECT r.id AS ReferenceId, r.title, c.number::int AS Number, c.quoted_words::int AS QuotedWords,
                   c.lyric_words::int AS LyricWords, c.figure_count::int AS FigureCount,
                   c.needs_permission AS NeedsPermission, c.status AS Status
            FROM reference_clearance c JOIN reference r ON r.id = c.reference_id
            WHERE c.book_id = @bookId AND c.use_count > 0
            ORDER BY c.number
            """,
            new { bookId }, transaction, cancellationToken: cancellationToken));
        await transaction.RollbackAsync(cancellationToken);

        var sources = rows.Select(r => ToDto(r, effective)).ToList();
        return new RulesImpactDto(effective, sources.Count(s => s.AtRisk), sources);
    }

    // Which rule applies is the view's call (NeedsPermission, Status); this only words it.
    private static RuleSourceDto ToDto(SourceRow row, int limit)
    {
        var reason = Reason(row, limit);
        var why = reason switch
        {
            "quote_over_limit" => $"{row.QuotedWords} of {limit} words quoted",
            "lyric" => "Lyrics or poetry quoted",
            "figure" => "Figure without a licence that allows reuse",
            _ => row.QuotedWords > 0 ? $"{row.QuotedWords} of {limit} words quoted" : null,
        };
        return new RuleSourceDto(row.ReferenceId, row.Title, row.Number, row.QuotedWords, reason, row.Status == "risk", why);
    }

    private static string? Reason(SourceRow row, int limit)
    {
        if (!row.NeedsPermission)
        {
            return null;
        }
        if (row.QuotedWords > limit)
        {
            return "quote_over_limit";
        }
        return row.LyricWords > 0 ? "lyric" : "figure";
    }
}
